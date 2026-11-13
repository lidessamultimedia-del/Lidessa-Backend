# Lidessa-Backend

API en ASP.NET Core (C#) + Entity Framework Core + SQL Server para la plataforma Lidessa (CEET/LMS). Da soporte al frontend en [Lidessa-Frontend](https://github.com/lidessamultimedia-del/Lidessa-Frontend).

## Requisitos

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- SQL Server (Express sirve) corriendo localmente

## 1. Crear la base de datos

Abre SQL Server Management Studio (o Azure Data Studio), conéctate a tu instancia local, y ejecuta todo el contenido de [`database/schema.sql`](database/schema.sql). Eso crea `LidessaDB` con sus 30 tablas. Si ya tenías la base creada, corre en su lugar los scripts de [`database/migrations/`](database/migrations/) que no hayas aplicado, **en orden de fecha**.

> **Si usas `sqlcmd` en vez de SSMS**, pasa siempre `-f 65001 -I`:
> ```bash
> sqlcmd -S localhost -E -C -b -f 65001 -I -i database/migrations/2026-11-05_supplies.sql
> ```
> - `-f 65001`: los `.sql` están en UTF-8; sin esto `sqlcmd` los lee como ANSI y los acentos quedan dañados en la base (`BotiquÃ­n` en vez de `Botiquín`).
> - `-I`: activa `QUOTED_IDENTIFIER`. Sin esto, cualquier `UPDATE`/`DELETE` sobre `AppUser` falla con el error 1934 (por el índice filtrado) — incluido el `UPDATE` del paso 2.2.
>
> Para saber si te falta Suministros: `SELECT OBJECT_ID('dbo.SupplyOrder')` debe dar un número, no `NULL`. Sin las migraciones `2026-11-05_supplies.sql` y `2026-11-10_supply_order_delivery_tokens.sql`, además, el registro de clientes falla (la restricción `CK_AppUser_Role` todavía no admite `'cliente'`).

## 2. Configurar la cadena de conexión

La cadena de conexión **no va en ningún archivo del repo** (cada quien tiene su propia instancia de SQL Server local con un nombre distinto). Se guarda con `user-secrets`, desde la carpeta `Lidessa.Api`:

```bash
cd Lidessa.Api
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:Default" "Server=TU_SERVIDOR\TU_INSTANCIA;Database=LidessaDB;Trusted_Connection=True;TrustServerCertificate=True;"
```

Reemplaza `TU_SERVIDOR\TU_INSTANCIA` por el nombre de tu propia instancia (ej. `DESKTOP-XXXXX\SQLEXPRESS`). Esto solo se hace una vez por máquina.

## 2.1. Configurar la clave del JWT

El login firma tokens JWT con una clave secreta que tampoco va en el repo. Se guarda igual que la cadena de conexión:

```bash
dotnet user-secrets set "Jwt:Key" "una-clave-larga-y-aleatoria"
```

Usa cualquier cadena aleatoria de al menos 32 caracteres. Sin esto, `dotnet run` falla al arrancar con un error explicando qué falta.

## 2.2. Crear el primer administrador

`POST /api/auth/register` sin sesión solo crea cuentas de **estudiante** (CEET) o **cliente** (tienda de Suministros); las de profesor o administrador solo las puede crear un admin logueado. Para el primer admin de una base nueva:

1. Regístrate normal (desde la página de registro o con `POST /api/auth/register` y `"role": "estudiante"`).
2. Promueve esa cuenta desde SQL Server:
   ```sql
   UPDATE LidessaDB.dbo.AppUser SET Role = 'admin' WHERE Email = 'tu-correo@ejemplo.com';
   -- La cuenta también tiene su rol original en AppUserRole (cuentas vinculadas);
   -- sin esto quedaría como admin + estudiante.
   UPDATE r SET r.Role = 'admin'
   FROM LidessaDB.dbo.AppUserRole r JOIN LidessaDB.dbo.AppUser u ON u.Id = r.UserId
   WHERE u.Email = 'tu-correo@ejemplo.com' AND r.Role = 'estudiante';
   ```
3. Vuelve a iniciar sesión (el rol va dentro del token). Desde ahí ese admin puede crear profesores y otros admins.

## 3. Correr el proyecto

```bash
cd Lidessa.Api
dotnet run
```

La consola va a mostrar algo como `Now listening on: http://localhost:5144` (el puerto puede variar). Abre esa misma dirección en el navegador — Swagger carga directo ahí, mostrando todos los endpoints disponibles.

Para confirmar que la conexión a la base quedó bien, prueba `GET /api/health` desde Swagger: debe responder `{"status":"ok","database":"connected"}`.

## 4. Pruebas

**Integración (rápidas, sin SQL Server):** levantan la API real contra una base EF InMemory.

```bash
dotnet test
```

**End-to-end contra el backend real** ([`e2e/roles-supplies.e2e.mjs`](e2e/roles-supplies.e2e.mjs)): recorre los 4 roles (admin, profesor, estudiante, cliente), una cuenta con rol doble (estudiante + cliente) y el ciclo completo de Suministros, incluida una prueba de concurrencia sobre el último stock. Requiere la API corriendo (`dotnet run`), Node 18+ y `sqlcmd` (para leer los tokens que en producción llegan por correo).

```bash
node e2e/roles-supplies.e2e.mjs          # crea sus propias cuentas/productos con prefijo e2e… y los borra al terminar
node e2e/roles-supplies.e2e.mjs --keep   # los deja en la base para revisarlos
```

Variables opcionales: `API_URL` (default `http://localhost:5144`), `SQL_SERVER` (default `localhost`), `SQL_DB` (default `LidessaDB`). Sale con código 1 si algo falla. Úsalo solo contra tu base local: escribe y borra datos.

> InMemory no es transaccional: si un `SaveChanges` choca por concurrencia, lo que iba antes en el mismo lote queda escrito. Por eso las pruebas de concurrencia de `dotnet test` solo verifican respuestas, stock y estado; que no queden pedidos huérfanos lo verifica el e2e contra SQL Server.

## Estructura del proyecto

```
Lidessa.Api/
├── Controllers/       # Endpoints HTTP
├── Services/          # Lógica de negocio
├── Data/
│   ├── AppDbContext.cs
│   └── Configurations/   # Configuración EF Core (una clase por entidad)
├── Models/             # Entidades (una clase por tabla)
└── Program.cs
```

## Flujo de trabajo con Git

- Cada quien trabaja y sube cambios **solo a su propia rama** (`cristian` o `santiago`).
- Para integrar cambios: abre un Pull Request de tu rama hacia `develop`, compáralo, y mergéalo cuando esté listo.
- Nunca se hace push directo a `develop` ni a `main`.
- Cuando el otro mergea algo a `develop`, jala esos cambios a tu rama:
  ```bash
  git checkout develop
  git pull origin develop
  git checkout tu-rama
  git merge develop
  ```
