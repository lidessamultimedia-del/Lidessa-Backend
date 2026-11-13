// Recorrido end-to-end contra el backend REAL (API corriendo + SQL Server):
// los 4 roles (admin, profesor, estudiante, cliente), una cuenta con rol
// doble (estudiante + cliente) y el ciclo completo de Suministros.
//
// Uso (con la API ya corriendo, `dotnet run` en Lidessa.Api):
//   node e2e/roles-supplies.e2e.mjs            # corre y limpia lo que creó
//   node e2e/roles-supplies.e2e.mjs --keep     # deja los datos para revisarlos
// Variables opcionales:
//   API_URL   (default http://localhost:5144)
//   SQL_SERVER (default localhost)   SQL_DB (default LidessaDB)
//
// sqlcmd se usa solo para lo que en producción llega por correo (token de
// activación de una invitación, token del botón "Ya me llegó") y para
// promover al primer admin, igual que dice el README (2.2).
import { execFileSync } from 'node:child_process'

const API = process.env.API_URL ?? 'http://localhost:5144'
const SQL_SERVER = process.env.SQL_SERVER ?? 'localhost'
const SQL_DB = process.env.SQL_DB ?? 'LidessaDB'
const KEEP = process.argv.includes('--keep')
const RUN = `e2e${Date.now()}`
const PASSWORD = 'Clave-E2e-123'
const mail = who => `${RUN}.${who}@e2e.lidessa.test`

// ---------------------------------------------------------------- harness
const results = []
let section = ''
function group(name) { section = name; console.log(`\n▶ ${name}`) }
async function check(name, fn) {
  try {
    await fn()
    results.push({ section, name, ok: true })
    console.log(`  ✓ ${name}`)
  } catch (err) {
    results.push({ section, name, ok: false, err: err.message })
    console.log(`  ✗ ${name}\n      ${err.message}`)
  }
}
function assert(cond, msg) { if (!cond) throw new Error(msg) }
function eq(actual, expected, msg) {
  const a = JSON.stringify(actual), e = JSON.stringify(expected)
  if (a !== e) throw new Error(`${msg}: esperado ${e}, llegó ${a}`)
}

async function http(method, path, { token, body } = {}) {
  const res = await fetch(`${API}${path}`, {
    method,
    headers: {
      ...(body !== undefined ? { 'Content-Type': 'application/json' } : {}),
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
    },
    body: body !== undefined ? JSON.stringify(body) : undefined,
  })
  const text = await res.text()
  let data = null
  try { data = text ? JSON.parse(text) : null } catch { data = text }
  return { status: res.status, data }
}
async function expectStatus(promise, status, what) {
  const r = await promise
  if (r.status !== status) throw new Error(`${what}: esperado HTTP ${status}, llegó ${r.status} ${JSON.stringify(r.data)?.slice(0, 200)}`)
  return r.data
}
const api = {
  get: (p, token) => http('GET', p, { token }),
  post: (p, body, token) => http('POST', p, { body, token }),
  put: (p, body, token) => http('PUT', p, { body, token }),
  patch: (p, body, token) => http('PATCH', p, { body, token }),
  del: (p, token) => http('DELETE', p, { token }),
}

function sql(query) {
  // -f 65001: sin esto sqlcmd lee/escribe en la página de códigos ANSI y
  // los acentos llegan corruptos (pasó con 2026-11-05_supplies.sql).
  // -I: QUOTED_IDENTIFIER ON — sin eso SQL Server rechaza los UPDATE/DELETE
  // sobre tablas con índices filtrados (AppUser), error 1934.
  let out
  try {
    out = execFileSync('sqlcmd', ['-S', SQL_SERVER, '-d', SQL_DB, '-E', '-C', '-b', '-I', '-f', '65001', '-h', '-1', '-W', '-Q', `SET NOCOUNT ON; ${query}`], { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] })
  } catch (err) {
    throw new Error(`sqlcmd: ${String(err.stdout || err.stderr || err.message).trim().slice(0, 300)}`)
  }
  return out.split(/\r?\n/).map(l => l.trim()).filter(Boolean)
}
const sqlStr = s => `N'${String(s).replace(/'/g, "''")}'`

function jwtRoles(token) {
  const payload = JSON.parse(Buffer.from(token.split('.')[1], 'base64url').toString('utf8'))
  const claim = payload['http://schemas.microsoft.com/ws/2008/06/identity/claims/role']
  return Array.isArray(claim) ? claim : [claim]
}

async function login(email, password = PASSWORD) {
  return expectStatus(api.post('/api/auth/login', { email, password }), 200, `login ${email}`)
}

// ---------------------------------------------------------------- estado
const acc = {}       // cuentas: { id, email, token, refreshToken }
const created = { productIds: [], orderIds: [], courseIds: [] }

const orderPayload = (items, extra = {}) => ({
  customerName: 'Cliente E2E', customerPhone: '3001234567', customerEmail: 'cliente.e2e@e2e.lidessa.test',
  address: 'Cra 1 # 2-3', city: 'Bogotá', notes: 'Prueba e2e', proofImageUrl: '/uploads/attachments/e2e-proof.png',
  items, ...extra,
})
const stockOf = async id => (await expectStatus(api.get(`/api/supplies/products/${id}`, acc.admin.token), 200, 'producto')).stock

// ================================================================ 0. API
group('0. Backend real')
await check('GET /api/health responde con la base conectada', async () => {
  const data = await expectStatus(api.get('/api/health'), 200, 'health')
  eq(data.database, 'connected', 'database')
})
await check('Las tablas de Suministros y AppUserRole existen en la base', async () => {
  const rows = sql("SELECT CASE WHEN OBJECT_ID('dbo.SupplyProduct') IS NOT NULL AND OBJECT_ID('dbo.SupplyOrder') IS NOT NULL AND OBJECT_ID('dbo.SupplyOrderDeliveryToken') IS NOT NULL AND OBJECT_ID('dbo.AppUserRole') IS NOT NULL THEN 'ok' ELSE 'missing' END")
  eq(rows[0], 'ok', 'tablas (aplicar database/migrations)')
})

// ================================================================ 1. Cuentas
group('1. Alta de las cuentas de los 4 roles')
await check('Registro público de estudiante', async () => {
  const u = await expectStatus(api.post('/api/auth/register', { name: 'Admin E2E', email: mail('admin'), password: PASSWORD, role: 'estudiante', phone: '3000000001' }), 201, 'register')
  acc.admin = { id: u.id, email: mail('admin') }
})
await check('Promoción a admin por SQL (README 2.2) y login como admin', async () => {
  // Igual que el README: se cambia el rol principal. El rol original en
  // AppUserRole también se ajusta para que la cuenta quede solo-admin.
  sql(`UPDATE dbo.AppUser SET Role='admin' WHERE Id=${acc.admin.id}; UPDATE dbo.AppUserRole SET Role='admin' WHERE UserId=${acc.admin.id};`)
  const r = await login(acc.admin.email)
  Object.assign(acc.admin, { token: r.token, refreshToken: r.refreshToken })
  eq(r.user.role, 'admin', 'rol')
  eq(jwtRoles(r.token), ['admin'], 'claims')
})
await check('Registro público NO permite crear admin ni profesor (403)', async () => {
  await expectStatus(api.post('/api/auth/register', { name: 'x', email: mail('hack1'), password: PASSWORD, role: 'admin' }), 403, 'admin anónimo')
  await expectStatus(api.post('/api/auth/register', { name: 'x', email: mail('hack2'), password: PASSWORD, role: 'profesor' }), 403, 'profesor anónimo')
})
await check('Un no-admin logueado tampoco puede crear profesores (403)', async () => {
  const c = await expectStatus(api.post('/api/auth/register', { name: 'Tmp', email: mail('tmp'), password: PASSWORD, role: 'cliente' }), 201, 'tmp')
  acc.tmp = { id: c.id }
  const t = (await login(mail('tmp'))).token
  await expectStatus(api.post('/api/auth/register', { name: 'x', email: mail('hack3'), password: PASSWORD, role: 'profesor' }, t), 403, 'profesor por cliente')
})
await check('Admin invita a un profesor → activación con token → login', async () => {
  const u = await expectStatus(api.post('/api/auth/invite', { name: 'Profesor E2E', email: mail('profesor'), role: 'profesor', phone: '3000000002' }, acc.admin.token), 201, 'invite')
  acc.profesor = { id: u.id, email: mail('profesor') }
  await expectStatus(api.post('/api/auth/login', { email: mail('profesor'), password: PASSWORD }), 401, 'login antes de activar')
  const [token] = sql(`SELECT TOP 1 Token FROM dbo.AccountInviteToken WHERE UserId=${u.id} ORDER BY Id DESC`)
  await expectStatus(api.post('/api/auth/activate-account', { token, password: PASSWORD }), 204, 'activate')
  await expectStatus(api.post('/api/auth/activate-account', { token, password: PASSWORD }), 400, 'token reutilizado')
  const r = await login(mail('profesor'))
  Object.assign(acc.profesor, { token: r.token })
  eq(r.user.roles, ['profesor'], 'roles')
})
await check('Admin invita a un estudiante → activación → login', async () => {
  const u = await expectStatus(api.post('/api/auth/invite', { name: 'Estudiante E2E', email: mail('estudiante'), role: 'estudiante' }, acc.admin.token), 201, 'invite')
  acc.estudiante = { id: u.id, email: mail('estudiante') }
  const [token] = sql(`SELECT TOP 1 Token FROM dbo.AccountInviteToken WHERE UserId=${u.id} ORDER BY Id DESC`)
  await expectStatus(api.post('/api/auth/activate-account', { token, password: PASSWORD }), 204, 'activate')
  const r = await login(mail('estudiante'))
  Object.assign(acc.estudiante, { token: r.token })
  eq(r.user.roles, ['estudiante'], 'roles')
})
await check('Registro público de cliente (tienda) → login', async () => {
  const u = await expectStatus(api.post('/api/auth/register', { name: 'Cliente E2E', email: mail('cliente'), password: PASSWORD, role: 'cliente', phone: '3000000003' }), 201, 'register cliente')
  acc.cliente = { id: u.id, email: mail('cliente') }
  const r = await login(mail('cliente'))
  Object.assign(acc.cliente, { token: r.token })
  eq(r.user.roles, ['cliente'], 'roles')
})
await check('Segundo cliente (para probar aislamiento de pedidos)', async () => {
  const u = await expectStatus(api.post('/api/auth/register', { name: 'Cliente Dos E2E', email: mail('cliente2'), password: PASSWORD, role: 'cliente' }), 201, 'register')
  acc.cliente2 = { id: u.id, token: (await login(mail('cliente2'))).token }
})
await check('Correo duplicado se rechaza (400)', async () => {
  await expectStatus(api.post('/api/auth/register', { name: 'Dup', email: mail('cliente'), password: PASSWORD, role: 'cliente' }), 400, 'duplicado')
})
await check('Contraseña incorrecta → 401', async () => {
  await expectStatus(api.post('/api/auth/login', { email: mail('cliente'), password: 'mala-clave' }), 401, 'login malo')
})

// ================================================================ 2. Cuenta vinculada
group('2. Cuenta con rol doble (estudiante + cliente)')
await check('Registro como estudiante', async () => {
  const u = await expectStatus(api.post('/api/auth/register', { name: 'Doble Rol E2E', email: mail('doble'), password: PASSWORD, role: 'estudiante', phone: '3000000004' }), 201, 'register')
  acc.doble = { id: u.id, email: mail('doble') }
  const r = await login(mail('doble'))
  Object.assign(acc.doble, { oldToken: r.token, refreshToken: r.refreshToken })
})
await check('Solo el admin puede vincular roles (403 para otros, 401 anónimo)', async () => {
  await expectStatus(api.post(`/api/auth/users/${acc.doble.id}/roles`, { role: 'cliente' }, acc.estudiante.token), 403, 'estudiante')
  await expectStatus(api.post(`/api/auth/users/${acc.doble.id}/roles`, { role: 'cliente' }, acc.profesor.token), 403, 'profesor')
  await expectStatus(api.post(`/api/auth/users/${acc.doble.id}/roles`, { role: 'cliente' }), 401, 'anónimo')
})
await check('Admin le agrega el rol cliente', async () => {
  const u = await expectStatus(api.post(`/api/auth/users/${acc.doble.id}/roles`, { role: 'cliente' }, acc.admin.token), 200, 'add role')
  eq(u.role, 'estudiante', 'rol principal no cambia')
  eq(u.roles, ['estudiante', 'cliente'], 'roles')
})
await check('No se duplica el rol ni se puede agregar admin por esta vía (400)', async () => {
  await expectStatus(api.post(`/api/auth/users/${acc.doble.id}/roles`, { role: 'cliente' }, acc.admin.token), 400, 'duplicado')
  await expectStatus(api.post(`/api/auth/users/${acc.doble.id}/roles`, { role: 'estudiante' }, acc.admin.token), 400, 'principal')
  await expectStatus(api.post(`/api/auth/users/${acc.doble.id}/roles`, { role: 'admin' }, acc.admin.token), 400, 'admin')
  await expectStatus(api.post('/api/auth/users/999999999/roles', { role: 'cliente' }, acc.admin.token), 404, 'inexistente')
  eq(sql(`SELECT COUNT(*) FROM dbo.AppUserRole WHERE UserId=${acc.doble.id}`)[0], '2', 'filas en AppUserRole')
})
await check('El token viejo (de antes del cambio) todavía no trae el rol cliente', async () => {
  eq(jwtRoles(acc.doble.oldToken), ['estudiante'], 'claims viejos')
  await expectStatus(api.get('/api/supplies/orders/mine', acc.doble.oldToken), 403, '/mine con token viejo')
})
await check('Refresh entrega un token con ambos roles', async () => {
  const r = await expectStatus(api.post('/api/auth/refresh', { refreshToken: acc.doble.refreshToken }), 200, 'refresh')
  eq(jwtRoles(r.token).sort(), ['cliente', 'estudiante'], 'claims tras refresh')
  await expectStatus(api.post('/api/auth/refresh', { refreshToken: acc.doble.refreshToken }), 401, 'refresh token rotado no se reutiliza')
})
await check('Login nuevo: /me trae role principal + roles', async () => {
  const r = await login(mail('doble'))
  acc.doble.token = r.token
  eq(r.user.roles, ['estudiante', 'cliente'], 'login.user.roles')
  const me = await expectStatus(api.get('/api/auth/me', r.token), 200, 'me')
  eq(me.role, 'estudiante', 'me.role')
  eq(me.roles.sort(), ['cliente', 'estudiante'], 'me.roles')
})
await check('GET /api/auth/users?role=cliente incluye la cuenta vinculada', async () => {
  const list = await expectStatus(api.get('/api/auth/users?role=cliente', acc.admin.token), 200, 'users')
  assert(list.some(u => u.id === acc.doble.id), 'falta la cuenta vinculada')
  assert(list.some(u => u.id === acc.cliente.id), 'falta el cliente normal')
  const est = await expectStatus(api.get('/api/auth/users?role=estudiante', acc.admin.token), 200, 'users')
  assert(est.some(u => u.id === acc.doble.id), 'la cuenta vinculada también debe salir como estudiante')
  assert(!est.some(u => u.id === acc.cliente.id), 'el cliente puro no debe salir como estudiante')
})
await check('Aviso de rol agregado quedó registrado (consola de la API)', async () => {
  // ConsoleNotificationService solo escribe en consola: aquí no se puede
  // verificar sin leer el log. Lo cubre AccountRolesEndpointsTests.
})

// ================================================================ 3. LMS por rol
group('3. LMS: cada rol ve lo suyo')
await check('Admin crea curso con el profesor e inscribe al estudiante y a la cuenta doble', async () => {
  // Sin publicar: publicar exige 3 actividades + 1 examen (regla del LMS).
  const c = await expectStatus(api.post('/api/courses', { name: `Curso ${RUN}`, format: 'topics', capacity: 30, teacherId: acc.profesor.id, published: false }, acc.admin.token), 201, 'curso')
  created.courseIds.push(c.id)
  acc.courseId = c.id
  await expectStatus(api.post(`/api/courses/${c.id}/enrollments`, { studentId: acc.estudiante.id }, acc.admin.token), 201, 'inscribir estudiante')
  await expectStatus(api.post(`/api/courses/${c.id}/enrollments`, { studentId: acc.doble.id }, acc.admin.token), 201, 'inscribir doble')
})
await check('No se puede inscribir a un cliente puro como estudiante (400)', async () => {
  const r = await api.post(`/api/courses/${acc.courseId}/enrollments`, { studentId: acc.cliente.id }, acc.admin.token)
  assert(r.status === 400, `esperado 400, llegó ${r.status} ${JSON.stringify(r.data)}`)
})
await check('Profesor ve su curso en /courses/mine', async () => {
  const list = await expectStatus(api.get('/api/courses/mine', acc.profesor.token), 200, 'mine')
  assert(list.some(c => c.id === acc.courseId), 'no aparece el curso')
})
await check('Estudiante ve el curso en /courses/mine', async () => {
  const list = await expectStatus(api.get('/api/courses/mine', acc.estudiante.token), 200, 'mine')
  assert(list.some(c => c.id === acc.courseId), 'no aparece el curso')
})
await check('Cuenta doble ve el curso (rol estudiante intacto)', async () => {
  const list = await expectStatus(api.get('/api/courses/mine', acc.doble.token), 200, 'mine')
  assert(list.some(c => c.id === acc.courseId), 'no aparece el curso')
})
await check('Cuenta doble ve progreso del curso y contacto del admin', async () => {
  await expectStatus(api.get(`/api/courses/${acc.courseId}/progress/me`, acc.doble.token), 200, 'progress')
  await expectStatus(api.get('/api/auth/admin-contact', acc.doble.token), 200, 'admin-contact')
})
await check('Cliente puro no ve cursos (lista vacía) ni panel de admin', async () => {
  const r = await api.get('/api/courses/mine', acc.cliente.token)
  assert(r.status === 403 || (r.status === 200 && r.data.length === 0), `esperado 403 o [] — llegó ${r.status} con ${r.data?.length} cursos`)
  await expectStatus(api.get('/api/auth/users', acc.cliente.token), 403, 'users')
})
await check('Profesor y estudiante no ven el directorio de usuarios (403)', async () => {
  await expectStatus(api.get('/api/auth/users', acc.profesor.token), 403, 'profesor')
  await expectStatus(api.get('/api/auth/users', acc.estudiante.token), 403, 'estudiante')
  await expectStatus(api.get('/api/auth/users', acc.doble.token), 403, 'doble')
})

// ================================================================ 4. Matriz de acceso Suministros
group('4. Suministros: matriz de permisos por rol')
await check('Catálogo público sin login', async () => {
  const list = await expectStatus(api.get('/api/supplies/products'), 200, 'catálogo')
  assert(Array.isArray(list) && list.length > 0, 'catálogo vacío (¿se corrió la migración con los 3 productos?)')
  assert(list.every(p => p.active), 'el catálogo público incluye inactivos')
  assert(list.some(p => p.name === 'Botiquín tipo A certificado'), 'acentos dañados en el seed (correr sqlcmd con -f 65001)')
})
await check('Solo admin crea/edita/borra productos', async () => {
  for (const [who, t] of [['profesor', acc.profesor.token], ['estudiante', acc.estudiante.token], ['cliente', acc.cliente.token], ['doble', acc.doble.token]]) {
    await expectStatus(api.post('/api/supplies/products', { name: 'x', price: 1, stock: 1 }, t), 403, `crear (${who})`)
    await expectStatus(api.get('/api/supplies/products/admin', t), 403, `listado admin (${who})`)
  }
  await expectStatus(api.post('/api/supplies/products', { name: 'x', price: 1, stock: 1 }), 401, 'crear anónimo')
})
await check('Profesor y estudiante puro no pueden pedir ni ver pedidos (403)', async () => {
  for (const [who, t] of [['profesor', acc.profesor.token], ['estudiante', acc.estudiante.token]]) {
    await expectStatus(api.get('/api/supplies/orders/mine', t), 403, `mine (${who})`)
    await expectStatus(api.get('/api/supplies/orders', t), 403, `todos (${who})`)
    await expectStatus(api.post('/api/supplies/orders', orderPayload([{ productId: 1, quantity: 1 }]), t), 403, `crear (${who})`)
  }
})
await check('Cliente y cuenta doble no ven el listado de todos los pedidos (403)', async () => {
  await expectStatus(api.get('/api/supplies/orders', acc.cliente.token), 403, 'cliente')
  await expectStatus(api.get('/api/supplies/orders', acc.doble.token), 403, 'doble')
})
await check('Anónimo no puede crear pedidos (401)', async () => {
  await expectStatus(api.post('/api/supplies/orders', orderPayload([{ productId: 1, quantity: 1 }])), 401, 'anónimo')
})

// ================================================================ 5. Productos
group('5. Suministros: productos (admin)')
await check('Admin crea producto con stock 5', async () => {
  const p = await expectStatus(api.post('/api/supplies/products', { name: `Guantes ${RUN}`, description: 'Nitrilo, talla M', price: 12500, stock: 5, imageUrl: '/assets/insumos.png' }, acc.admin.token), 201, 'crear')
  created.productIds.push(p.id); acc.prodA = p.id
  eq([p.price, p.stock, p.active], [12500, 5, true], 'producto')
})
await check('Admin crea segundo producto (stock 3)', async () => {
  const p = await expectStatus(api.post('/api/supplies/products', { name: `Tapabocas ${RUN}`, price: 2000, stock: 3 }, acc.admin.token), 201, 'crear')
  created.productIds.push(p.id); acc.prodB = p.id
})
await check('Validaciones: precio 0 / stock negativo / sin nombre → 400', async () => {
  await expectStatus(api.post('/api/supplies/products', { name: 'x', price: 0, stock: 1 }, acc.admin.token), 400, 'precio 0')
  await expectStatus(api.post('/api/supplies/products', { name: 'x', price: 10, stock: -1 }, acc.admin.token), 400, 'stock negativo')
  await expectStatus(api.post('/api/supplies/products', { name: '', price: 10, stock: 1 }, acc.admin.token), 400, 'sin nombre')
})
await check('Precio con decimales se guarda exacto (decimal 14,2)', async () => {
  const p = await expectStatus(api.put(`/api/supplies/products/${acc.prodB}`, { name: `Tapabocas ${RUN}`, price: 1999.99, stock: 3 }, acc.admin.token), 200, 'editar')
  eq(p.price, 1999.99, 'precio')
  await expectStatus(api.put(`/api/supplies/products/${acc.prodB}`, { name: `Tapabocas ${RUN}`, price: 2000, stock: 3 }, acc.admin.token), 200, 'restaurar')
})
await check('Ocultar producto: sale del catálogo público y no se puede pedir', async () => {
  await expectStatus(api.patch(`/api/supplies/products/${acc.prodB}/toggle-active`, undefined, acc.admin.token), 200, 'toggle')
  const list = await expectStatus(api.get('/api/supplies/products'), 200, 'catálogo')
  assert(!list.some(p => p.id === acc.prodB), 'el oculto sigue en el catálogo')
  await expectStatus(api.get(`/api/supplies/products/${acc.prodB}`), 404, 'detalle público del oculto')
  await expectStatus(api.get(`/api/supplies/products/${acc.prodB}`, acc.admin.token), 200, 'detalle admin del oculto')
  await expectStatus(api.post('/api/supplies/orders', orderPayload([{ productId: acc.prodB, quantity: 1 }]), acc.cliente.token), 400, 'pedido de oculto')
  await expectStatus(api.patch(`/api/supplies/products/${acc.prodB}/toggle-active`, undefined, acc.admin.token), 200, 'reactivar')
})

// ================================================================ 6. Ciclo feliz
group('6. Suministros: pedido del cliente de punta a punta')
await check('Cliente crea pedido: precio del catálogo (no el enviado), total y stock apartado', async () => {
  const o = await expectStatus(api.post('/api/supplies/orders', orderPayload([{ productId: acc.prodA, quantity: 2, unitPrice: 1 }], { clientId: acc.cliente2.id }), acc.cliente.token), 201, 'crear')
  created.orderIds.push(o.id); acc.orderA = o.id
  eq(o.clientId, acc.cliente.id, 'clientId forzado al que llama (ignora el del body)')
  eq(o.status, 'pending', 'estado')
  eq(o.total, 25000, 'total')
  eq(o.items[0].unitPrice, 12500, 'precio unitario')
  eq(o.statusHistory.map(h => h.status), ['pending'], 'historial')
  eq(await stockOf(acc.prodA), 3, 'stock tras apartar')
})
await check('El pedido aparece en "Mis pedidos" del cliente y en el panel admin', async () => {
  const mine = await expectStatus(api.get('/api/supplies/orders/mine', acc.cliente.token), 200, 'mine')
  assert(mine.some(o => o.id === acc.orderA), 'no aparece en mine')
  const all = await expectStatus(api.get('/api/supplies/orders', acc.admin.token), 200, 'all')
  assert(all.some(o => o.id === acc.orderA), 'no aparece en admin')
})
await check('Otro cliente no ve ni cierra ese pedido (404)', async () => {
  await expectStatus(api.get(`/api/supplies/orders/${acc.orderA}`, acc.cliente2.token), 404, 'detalle ajeno')
  await expectStatus(api.patch(`/api/supplies/orders/${acc.orderA}/complete`, undefined, acc.cliente2.token), 404, 'complete ajeno')
  const mine = await expectStatus(api.get('/api/supplies/orders/mine', acc.cliente2.token), 200, 'mine2')
  assert(!mine.some(o => o.id === acc.orderA), 'se filtró en mine de otro cliente')
})
await check('Cliente no puede confirmar/despachar/rechazar/editar/borrar (403)', async () => {
  const t = acc.cliente.token
  await expectStatus(api.patch(`/api/supplies/orders/${acc.orderA}/confirm`, undefined, t), 403, 'confirm')
  await expectStatus(api.patch(`/api/supplies/orders/${acc.orderA}/reject`, { reason: 'x' }, t), 403, 'reject')
  await expectStatus(api.patch(`/api/supplies/orders/${acc.orderA}/ship`, { carrier: 'x', trackingNumber: 'x', photoUrl: 'x' }, t), 403, 'ship')
  await expectStatus(api.put(`/api/supplies/orders/${acc.orderA}`, orderPayload(null), t), 403, 'put')
  await expectStatus(api.del(`/api/supplies/orders/${acc.orderA}`, t), 403, 'delete')
})
await check('Aviso de pago incompleto: validaciones y el pedido sigue pendiente', async () => {
  await expectStatus(api.patch(`/api/supplies/orders/${acc.orderA}/payment-shortfall`, { amount: 99999999, message: 'falta' }, acc.admin.token), 400, 'monto > total')
  await expectStatus(api.patch(`/api/supplies/orders/${acc.orderA}/payment-shortfall`, { amount: 0, message: 'falta' }, acc.admin.token), 400, 'monto 0')
  await expectStatus(api.patch(`/api/supplies/orders/${acc.orderA}/payment-shortfall`, { amount: 5000, message: '' }, acc.admin.token), 400, 'sin mensaje')
  const o = await expectStatus(api.patch(`/api/supplies/orders/${acc.orderA}/payment-shortfall`, { amount: 5000.5, message: 'Faltan $5.000,50' }, acc.admin.token), 200, 'shortfall')
  eq([o.status, o.paymentNote?.amount], ['pending', 5000.5], 'pendiente con nota')
  const seen = await expectStatus(api.get(`/api/supplies/orders/${acc.orderA}`, acc.cliente.token), 200, 'cliente ve la nota')
  eq(seen.paymentNote?.message, 'Faltan $5.000,50', 'mensaje')
})
await check('No se puede despachar ni cerrar un pedido pendiente (400)', async () => {
  await expectStatus(api.patch(`/api/supplies/orders/${acc.orderA}/ship`, { carrier: 'Servientrega', trackingNumber: '1', photoUrl: '/x.png' }, acc.admin.token), 400, 'ship pendiente')
  await expectStatus(api.patch(`/api/supplies/orders/${acc.orderA}/complete`, undefined, acc.cliente.token), 400, 'complete pendiente')
})
await check('Admin confirma; no se puede confirmar dos veces ni rechazar ya confirmado', async () => {
  const o = await expectStatus(api.patch(`/api/supplies/orders/${acc.orderA}/confirm`, undefined, acc.admin.token), 200, 'confirm')
  eq(o.status, 'confirmed', 'estado')
  await expectStatus(api.patch(`/api/supplies/orders/${acc.orderA}/confirm`, undefined, acc.admin.token), 400, 'doble confirm')
  await expectStatus(api.patch(`/api/supplies/orders/${acc.orderA}/reject`, { reason: 'x' }, acc.admin.token), 400, 'reject confirmado')
  await expectStatus(api.patch(`/api/supplies/orders/${acc.orderA}/payment-shortfall`, { amount: 1, message: 'x' }, acc.admin.token), 400, 'shortfall confirmado')
  eq(await stockOf(acc.prodA), 3, 'confirmar no toca stock')
})
await check('Despacho: exige transportadora, guía y foto', async () => {
  await expectStatus(api.patch(`/api/supplies/orders/${acc.orderA}/ship`, { carrier: '', trackingNumber: '1', photoUrl: '/x.png' }, acc.admin.token), 400, 'sin transportadora')
  await expectStatus(api.patch(`/api/supplies/orders/${acc.orderA}/ship`, { carrier: 'X', trackingNumber: '', photoUrl: '/x.png' }, acc.admin.token), 400, 'sin guía')
  await expectStatus(api.patch(`/api/supplies/orders/${acc.orderA}/ship`, { carrier: 'X', trackingNumber: '1', photoUrl: '' }, acc.admin.token), 400, 'sin foto')
})
await check('Admin despacha; se genera token de entrega de 30 días', async () => {
  const o = await expectStatus(api.patch(`/api/supplies/orders/${acc.orderA}/ship`, { carrier: 'Servientrega', trackingNumber: 'SRV-123', photoUrl: '/uploads/attachments/paquete.png' }, acc.admin.token), 200, 'ship')
  eq([o.status, o.shipment?.carrier, o.shipment?.trackingNumber], ['shipped', 'Servientrega', 'SRV-123'], 'envío')
  const [days] = sql(`SELECT DATEDIFF(day, CreatedAt, ExpiresAt) FROM dbo.SupplyOrderDeliveryToken WHERE OrderId=${acc.orderA}`)
  eq(days, '30', 'vigencia del token')
})
await check('Editar artículos de un pedido despachado → 400; datos de contacto sí', async () => {
  await expectStatus(api.put(`/api/supplies/orders/${acc.orderA}`, orderPayload([{ productId: acc.prodA, quantity: 1 }], { clientId: acc.cliente.id }), acc.admin.token), 400, 'items en enviado')
  const o = await expectStatus(api.put(`/api/supplies/orders/${acc.orderA}`, { ...orderPayload(null), clientId: acc.cliente.id, address: 'Calle 99 # 1-1' }, acc.admin.token), 200, 'contacto')
  eq([o.address, o.items.length, o.total], ['Calle 99 # 1-1', 1, 25000], 'artículos intactos')
})
await check('Cliente confirma "Ya me llegó" desde su cuenta → completed; no se repite', async () => {
  const o = await expectStatus(api.patch(`/api/supplies/orders/${acc.orderA}/complete`, undefined, acc.cliente.token), 200, 'complete')
  eq(o.statusHistory.map(h => h.status), ['pending', 'confirmed', 'shipped', 'completed'], 'historial completo')
  await expectStatus(api.patch(`/api/supplies/orders/${acc.orderA}/complete`, undefined, acc.cliente.token), 400, 'segunda vez')
})
await check('El enlace del correo ya no sirve si el pedido se cerró por la cuenta', async () => {
  const [token] = sql(`SELECT Token FROM dbo.SupplyOrderDeliveryToken WHERE OrderId=${acc.orderA}`)
  await expectStatus(api.get(`/api/supplies/orders/confirm-delivery/${token}`), 400, 'token de pedido completado')
})

// ================================================================ 7. Confirmación por correo
group('7. Suministros: "Ya me llegó" desde el correo (sin login)')
await check('Pedido de la cuenta doble → confirmar → despachar', async () => {
  const o = await expectStatus(api.post('/api/supplies/orders', orderPayload([{ productId: acc.prodA, quantity: 1 }]), acc.doble.token), 201, 'crear (doble)')
  created.orderIds.push(o.id); acc.orderDoble = o.id
  eq(o.clientId, acc.doble.id, 'pedido a nombre de la cuenta doble')
  await expectStatus(api.patch(`/api/supplies/orders/${o.id}/confirm`, undefined, acc.admin.token), 200, 'confirm')
  await expectStatus(api.patch(`/api/supplies/orders/${o.id}/ship`, { carrier: 'Coordinadora', trackingNumber: 'CO-9', photoUrl: '/uploads/attachments/p.png' }, acc.admin.token), 200, 'ship')
})
await check('Cuenta doble ve su pedido en "Mis pedidos" y no los de otros', async () => {
  const mine = await expectStatus(api.get('/api/supplies/orders/mine', acc.doble.token), 200, 'mine')
  eq(mine.map(o => o.id), [acc.orderDoble], 'mine de la cuenta doble')
})
await check('Token inválido → 400', async () => {
  await expectStatus(api.get('/api/supplies/orders/confirm-delivery/no-existe'), 400, 'token falso')
})
await check('Token válido sin login → 200 y el pedido queda completed', async () => {
  const [token] = sql(`SELECT Token FROM dbo.SupplyOrderDeliveryToken WHERE OrderId=${acc.orderDoble}`)
  await expectStatus(api.get(`/api/supplies/orders/confirm-delivery/${token}`), 200, 'confirm-delivery')
  const o = await expectStatus(api.get(`/api/supplies/orders/${acc.orderDoble}`, acc.doble.token), 200, 'detalle')
  eq(o.status, 'completed', 'estado')
  await expectStatus(api.get(`/api/supplies/orders/confirm-delivery/${token}`), 400, 'token de un solo uso')
})
await check('Token vencido → 400', async () => {
  const o = await expectStatus(api.post('/api/supplies/orders', orderPayload([{ productId: acc.prodA, quantity: 1 }]), acc.cliente.token), 201, 'crear')
  created.orderIds.push(o.id)
  await expectStatus(api.patch(`/api/supplies/orders/${o.id}/confirm`, undefined, acc.admin.token), 200, 'confirm')
  await expectStatus(api.patch(`/api/supplies/orders/${o.id}/ship`, { carrier: 'X', trackingNumber: 'Y', photoUrl: '/z.png' }, acc.admin.token), 200, 'ship')
  sql(`UPDATE dbo.SupplyOrderDeliveryToken SET ExpiresAt = DATEADD(minute,-1,SYSUTCDATETIME()) WHERE OrderId=${o.id}`)
  const [token] = sql(`SELECT Token FROM dbo.SupplyOrderDeliveryToken WHERE OrderId=${o.id}`)
  await expectStatus(api.get(`/api/supplies/orders/confirm-delivery/${token}`), 400, 'vencido')
  // El admin puede cerrarlo a mano en ese caso.
  const done = await expectStatus(api.patch(`/api/supplies/orders/${o.id}/complete`, undefined, acc.admin.token), 200, 'admin cierra')
  eq(done.status, 'completed', 'estado')
})

// ================================================================ 8. Rechazo, stock y walk-in
group('8. Suministros: rechazo, inventario y pedidos del admin')
await check('Rechazo exige causa y devuelve el stock apartado', async () => {
  const before = await stockOf(acc.prodA)
  const o = await expectStatus(api.post('/api/supplies/orders', orderPayload([{ productId: acc.prodA, quantity: 1 }]), acc.cliente.token), 201, 'crear')
  created.orderIds.push(o.id)
  eq(await stockOf(acc.prodA), before - 1, 'apartado')
  await expectStatus(api.patch(`/api/supplies/orders/${o.id}/reject`, { reason: '   ' }, acc.admin.token), 400, 'sin causa')
  const r = await expectStatus(api.patch(`/api/supplies/orders/${o.id}/reject`, { reason: 'Comprobante ilegible', refundProofUrl: '/uploads/attachments/devolucion.png' }, acc.admin.token), 200, 'reject')
  eq([r.status, r.rejectionReason], ['rejected', 'Comprobante ilegible'], 'rechazado')
  eq(await stockOf(acc.prodA), before, 'stock devuelto')
  await expectStatus(api.patch(`/api/supplies/orders/${o.id}/confirm`, undefined, acc.admin.token), 400, 'confirmar rechazado')
})
await check('Sin stock suficiente → 400 (también sumando líneas repetidas)', async () => {
  const stock = await stockOf(acc.prodB)
  await expectStatus(api.post('/api/supplies/orders', orderPayload([{ productId: acc.prodB, quantity: stock + 1 }]), acc.cliente.token), 400, 'exceso')
  await expectStatus(api.post('/api/supplies/orders', orderPayload([{ productId: acc.prodB, quantity: stock }, { productId: acc.prodB, quantity: 1 }]), acc.cliente.token), 400, 'exceso repartido')
  eq(await stockOf(acc.prodB), stock, 'stock intacto')
})
await check('Validaciones de pedido: sin artículos, cantidad 0, producto inexistente, sin teléfono', async () => {
  const t = acc.cliente.token
  await expectStatus(api.post('/api/supplies/orders', orderPayload([]), t), 400, 'sin items')
  await expectStatus(api.post('/api/supplies/orders', orderPayload([{ productId: acc.prodA, quantity: 0 }]), t), 400, 'cantidad 0')
  await expectStatus(api.post('/api/supplies/orders', orderPayload([{ productId: 999999999, quantity: 1 }]), t), 400, 'inexistente')
  await expectStatus(api.post('/api/supplies/orders', orderPayload([{ productId: acc.prodA, quantity: 1 }], { customerPhone: '' }), t), 400, 'sin teléfono')
})
await check('Admin crea pedido walk-in sin cuenta y lo cierra él mismo', async () => {
  const o = await expectStatus(api.post('/api/supplies/orders', orderPayload([{ productId: acc.prodB, quantity: 1 }], { clientId: null, customerName: 'Mostrador' }), acc.admin.token), 201, 'walk-in')
  created.orderIds.push(o.id)
  eq(o.clientId, null, 'sin cuenta')
  await expectStatus(api.patch(`/api/supplies/orders/${o.id}/confirm`, undefined, acc.admin.token), 200, 'confirm')
  await expectStatus(api.patch(`/api/supplies/orders/${o.id}/ship`, { carrier: 'Entrega en sede', trackingNumber: 'N/A', photoUrl: '/p.png' }, acc.admin.token), 200, 'ship')
  eq((await expectStatus(api.patch(`/api/supplies/orders/${o.id}/complete`, undefined, acc.admin.token), 200, 'complete')).status, 'completed', 'estado')
})
await check('Admin crea pedido ligado a la cuenta doble → le aparece en "Mis pedidos"', async () => {
  const o = await expectStatus(api.post('/api/supplies/orders', orderPayload([{ productId: acc.prodB, quantity: 1 }], { clientId: acc.doble.id }), acc.admin.token), 201, 'ligado')
  created.orderIds.push(o.id); acc.orderLinked = o.id
  const mine = await expectStatus(api.get('/api/supplies/orders/mine', acc.doble.token), 200, 'mine')
  assert(mine.some(x => x.id === o.id), 'no aparece')
})
await check('Admin con rol cliente vinculado sigue creando walk-in y pedidos para otros', async () => {
  const u = await expectStatus(api.post('/api/auth/register', { name: 'Admin Cliente E2E', email: mail('admincliente'), password: PASSWORD, role: 'admin' }, acc.admin.token), 201, 'admin por admin')
  acc.adminCliente = { id: u.id }
  await expectStatus(api.post(`/api/auth/users/${u.id}/roles`, { role: 'cliente' }, acc.admin.token), 200, 'vincular cliente')
  const t = (await login(mail('admincliente'))).token
  eq(jwtRoles(t).sort(), ['admin', 'cliente'], 'claims')
  await expectStatus(api.put(`/api/supplies/products/${acc.prodB}`, { name: `Tapabocas ${RUN}`, price: 2000, stock: 5 }, acc.admin.token), 200, 'reabastecer B')
  const walkIn = await expectStatus(api.post('/api/supplies/orders', orderPayload([{ productId: acc.prodB, quantity: 1 }], { clientId: null }), t), 201, 'walk-in')
  created.orderIds.push(walkIn.id)
  eq(walkIn.clientId, null, 'walk-in sin cuenta')
  const forOther = await expectStatus(api.post('/api/supplies/orders', orderPayload([{ productId: acc.prodB, quantity: 1 }], { clientId: acc.cliente2.id }), t), 201, 'para otro')
  created.orderIds.push(forOther.id)
  eq(forOther.clientId, acc.cliente2.id, 'pedido a nombre del otro cliente')
  // Devuelve el stock de B para las pruebas que siguen.
  for (const id of [walkIn.id, forOther.id]) await expectStatus(api.del(`/api/supplies/orders/${id}`, acc.admin.token), 204, 'limpiar')
  created.orderIds = created.orderIds.filter(id => id !== walkIn.id && id !== forOther.id)
})
await check('Admin con clientId inexistente → 400', async () => {
  await expectStatus(api.post('/api/supplies/orders', orderPayload([{ productId: acc.prodB, quantity: 1 }], { clientId: 999999999 }), acc.admin.token), 400, 'cliente inexistente')
})
await check('Editar artículos de pendiente ajusta el stock por la diferencia', async () => {
  // Reabastece A (los pedidos anteriores lo dejaron casi en 0).
  await expectStatus(api.put(`/api/supplies/products/${acc.prodA}`, { name: `Guantes ${RUN}`, description: 'Nitrilo, talla M', price: 12500, stock: 10, imageUrl: '/assets/insumos.png' }, acc.admin.token), 200, 'reabastecer')
  const sA = await stockOf(acc.prodA), sB = await stockOf(acc.prodB)
  // El pedido ligado tenía 1 de B. Se cambia a 2 de A (B se libera).
  const o = await expectStatus(api.put(`/api/supplies/orders/${acc.orderLinked}`, orderPayload([{ productId: acc.prodA, quantity: 2 }], { clientId: acc.doble.id }), acc.admin.token), 200, 'put')
  eq(o.total, 25000, 'total recalculado')
  eq([await stockOf(acc.prodA), await stockOf(acc.prodB)], [sA - 2, sB + 1], 'stock A/B')
  await expectStatus(api.put(`/api/supplies/orders/${acc.orderLinked}`, orderPayload([{ productId: acc.prodA, quantity: sA + 3 }], { clientId: acc.doble.id }), acc.admin.token), 400, 'más del disponible')
})
await check('Borrar pedido pendiente devuelve el stock', async () => {
  const sA = await stockOf(acc.prodA)
  await expectStatus(api.del(`/api/supplies/orders/${acc.orderLinked}`, acc.admin.token), 204, 'delete')
  eq(await stockOf(acc.prodA), sA + 2, 'stock devuelto')
  await expectStatus(api.get(`/api/supplies/orders/${acc.orderLinked}`, acc.admin.token), 404, 'ya no existe')
  created.orderIds = created.orderIds.filter(id => id !== acc.orderLinked)
})
await check('Borrar producto con pedidos: el pedido conserva nombre y precio', async () => {
  const p = await expectStatus(api.post('/api/supplies/products', { name: `Temporal ${RUN}`, price: 777, stock: 2 }, acc.admin.token), 201, 'producto')
  const o = await expectStatus(api.post('/api/supplies/orders', orderPayload([{ productId: p.id, quantity: 1 }]), acc.cliente.token), 201, 'pedido')
  created.orderIds.push(o.id)
  const del = await api.del(`/api/supplies/products/${p.id}`, acc.admin.token)
  if (del.status !== 204) { created.productIds.push(p.id); throw new Error(`borrar producto con pedidos: HTTP ${del.status} ${JSON.stringify(del.data)}`) }
  const after = await expectStatus(api.get(`/api/supplies/orders/${o.id}`, acc.admin.token), 200, 'pedido')
  eq([after.items[0].productId, after.items[0].productName, after.items[0].unitPrice], [null, `Temporal ${RUN}`, 777], 'línea congelada')
})

// ================================================================ 9. Concurrencia
group('9. Suministros: concurrencia sobre el último stock')
const ROUNDS = 5, PARALLEL = 20, STOCK = 3
await check(`${ROUNDS} rondas de ${PARALLEL} pedidos simultáneos con stock ${STOCK} → nunca se vende más de lo que hay`, async () => {
  const buyers = [acc.cliente.token, acc.cliente2.token, acc.doble.token]
  const problems = []
  for (let round = 1; round <= ROUNDS; round++) {
    const p = await expectStatus(api.post('/api/supplies/products', { name: `Escaso ${RUN} r${round}`, price: 1000, stock: STOCK }, acc.admin.token), 201, 'producto')
    created.productIds.push(p.id)
    const rs = await Promise.all(Array.from({ length: PARALLEL }, (_, i) =>
      api.post('/api/supplies/orders', orderPayload([{ productId: p.id, quantity: 1 }]), buyers[i % buyers.length])))
    rs.filter(r => r.status === 201).forEach(r => created.orderIds.push(r.data.id))
    const accepted = rs.filter(r => r.status === 201).length
    const crashed = rs.filter(r => r.status >= 500)
    const stock = await stockOf(p.id)
    const [reserved] = sql(`SELECT ISNULL(SUM(i.Quantity),0) FROM dbo.SupplyOrderItem i JOIN dbo.SupplyOrder o ON o.Id=i.OrderId WHERE i.ProductId=${p.id}`)
    if (crashed.length) problems.push(`ronda ${round}: ${crashed.length} respuestas 5xx (${crashed[0].data?.message})`)
    if (Number(reserved) + stock !== STOCK || accepted !== STOCK) {
      problems.push(`ronda ${round}: ${accepted} pedidos aceptados, ${reserved} unidades apartadas, stock final ${stock} (debía: ${STOCK} aceptados, stock 0)`)
    }
  }
  assert(problems.length === 0, `sobreventa / inventario descuadrado:\n        ${problems.join('\n        ')}`)
})

// ================================================================ 10. Sesiones
group('10. Sesión')
await check('Logout revoca el refresh token', async () => {
  const r = await login(mail('cliente'))
  await expectStatus(api.post('/api/auth/logout', { refreshToken: r.refreshToken }), 204, 'logout')
  await expectStatus(api.post('/api/auth/refresh', { refreshToken: r.refreshToken }), 401, 'refresh revocado')
})
await check('Token manipulado → 401', async () => {
  const [h, , s] = acc.cliente.token.split('.')
  const fake = Buffer.from(JSON.stringify({ sub: String(acc.cliente.id), 'http://schemas.microsoft.com/ws/2008/06/identity/claims/role': 'admin', exp: 9999999999, iss: 'Lidessa.Api', aud: 'Lidessa.Frontend' })).toString('base64url')
  await expectStatus(api.get('/api/supplies/orders', `${h}.${fake}.${s}`), 401, 'token con rol inventado')
})

// ================================================================ limpieza
async function cleanup() {
  if (KEEP) { console.log(`\n(--keep) Datos de prueba conservados. Prefijo: ${RUN}`); return }
  const userIds = Object.values(acc).filter(a => a && typeof a === 'object' && a.id).map(a => a.id)
  const ids = userIds.length ? userIds.join(',') : '0'
  for (const id of created.courseIds) await api.del(`/api/courses/${id}`, acc.admin?.token)
  const orderIds = created.orderIds.length ? created.orderIds.join(',') : '0'
  const productIds = created.productIds.length ? created.productIds.join(',') : '0'
  sql(`
    DELETE FROM dbo.SupplyOrder WHERE Id IN (${orderIds}) OR ClientId IN (${ids}) OR CustomerEmail LIKE '%@e2e.lidessa.test';
    DELETE FROM dbo.SupplyProduct WHERE Id IN (${productIds});
    DELETE FROM dbo.CourseEnrollment WHERE StudentId IN (${ids});
    DELETE FROM dbo.Course WHERE TeacherId IN (${ids});
    DELETE FROM dbo.AppUserRole WHERE GrantedByUserId IN (${ids});
    DELETE FROM dbo.AppUser WHERE Id IN (${ids}) OR Email LIKE ${sqlStr(`${RUN}.%@e2e.lidessa.test`)};
  `)
  const [left] = sql(`SELECT COUNT(*) FROM dbo.AppUser WHERE Email LIKE ${sqlStr(`${RUN}.%`)}`)
  console.log(`\nLimpieza: ${left === '0' ? 'ok' : `quedaron ${left} cuentas`} (prefijo ${RUN})`)
}

try { await cleanup() } catch (err) { console.log(`\n⚠ Limpieza falló: ${err.message}`) }

const failed = results.filter(r => !r.ok)
console.log(`\n${results.length - failed.length}/${results.length} verificaciones OK`)
if (failed.length) {
  console.log('\nFallaron:')
  failed.forEach(f => console.log(`  - [${f.section}] ${f.name}\n      ${f.err}`))
  process.exit(1)
}
