-- Para bases ya creadas con una versión anterior de schema.sql.
-- Cuentas vinculadas (ver 2.2): agrega AppUserRole para que una cuenta tenga
-- más de un rol, y copia el rol actual de cada cuenta como su primer rol.
-- AppUser.Role se mantiene como rol principal.
USE LidessaDB;
GO

IF OBJECT_ID('dbo.AppUserRole', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.AppUserRole (
        Id                   BIGINT IDENTITY(1,1) PRIMARY KEY,
        UserId               BIGINT NOT NULL CONSTRAINT FK_AppUserRole_User REFERENCES dbo.AppUser(Id) ON DELETE CASCADE,
        Role                 NVARCHAR(20) NOT NULL
            CONSTRAINT CK_AppUserRole_Role CHECK (Role IN ('admin','profesor','estudiante','cliente')),
        GrantedAt            DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        GrantedByUserId      BIGINT NULL CONSTRAINT FK_AppUserRole_GrantedBy REFERENCES dbo.AppUser(Id)
    );
    CREATE UNIQUE INDEX UX_AppUserRole_User_Role ON dbo.AppUserRole(UserId, Role);
END
GO

-- Backfill idempotente: el rol actual de cada cuenta pasa a ser su primer rol.
INSERT INTO dbo.AppUserRole (UserId, Role, GrantedAt)
SELECT u.Id, u.Role, u.CreatedAt
FROM dbo.AppUser u
WHERE NOT EXISTS (SELECT 1 FROM dbo.AppUserRole r WHERE r.UserId = u.Id AND r.Role = u.Role);
GO
