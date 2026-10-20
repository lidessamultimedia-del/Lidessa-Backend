-- Para bases ya creadas con una versión anterior de schema.sql.
-- Agrega la tabla AccountInviteToken (invitación de cuenta de profesor/estudiante sin contraseña).
USE LidessaDB;
GO

IF OBJECT_ID('dbo.AccountInviteToken', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.AccountInviteToken (
        Id                   BIGINT IDENTITY(1,1) PRIMARY KEY,
        UserId               BIGINT NOT NULL CONSTRAINT FK_AccountInviteToken_User REFERENCES dbo.AppUser(Id) ON DELETE CASCADE,
        Token                NVARCHAR(100) NOT NULL,
        ExpiresAt            DATETIME2 NOT NULL,
        UsedAt               DATETIME2 NULL,
        CreatedAt            DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
    CREATE UNIQUE INDEX UX_AccountInviteToken_Token ON dbo.AccountInviteToken(Token);
END
GO
