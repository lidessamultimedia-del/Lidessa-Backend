# Lidessa-Backend

API en ASP.NET Core (C#) + Entity Framework Core + SQL Server para la plataforma Lidessa (CEET/LMS). Da soporte al frontend en [Lidessa-Frontend](https://github.com/lidessamultimedia-del/Lidessa-Frontend).

## Requisitos

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- SQL Server (Express sirve) corriendo localmente

## 1. Crear la base de datos

Abre SQL Server Management Studio (o Azure Data Studio), conéctate a tu instancia local, y ejecuta todo el contenido de [`database/schema.sql`](database/schema.sql). Eso crea `LidessaDB` con las 24 tablas.

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

`POST /api/auth/register` sin sesión solo crea cuentas de **estudiante**; las de profesor o administrador solo las puede crear un admin logueado. Para el primer admin de una base nueva:

1. Regístrate normal (desde la página de registro o con `POST /api/auth/register` y `"role": "estudiante"`).
2. Promueve esa cuenta desde SQL Server:
   ```sql
   UPDATE LidessaDB.dbo.AppUser SET Role = 'admin' WHERE Email = 'tu-correo@ejemplo.com';
   ```
3. Vuelve a iniciar sesión (el rol va dentro del token). Desde ahí ese admin puede crear profesores y otros admins.

## 3. Correr el proyecto

```bash
cd Lidessa.Api
dotnet run
```

La consola va a mostrar algo como `Now listening on: http://localhost:5144` (el puerto puede variar). Abre esa misma dirección en el navegador — Swagger carga directo ahí, mostrando todos los endpoints disponibles.

Para confirmar que la conexión a la base quedó bien, prueba `GET /api/health` desde Swagger: debe responder `{"status":"ok","database":"connected"}`.

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
