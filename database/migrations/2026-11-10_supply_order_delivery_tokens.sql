-- Para bases ya creadas con una versión anterior de schema.sql.
-- Agrega la tabla de tokens de un solo uso para el botón "Ya me llegó" del
-- correo de envío (confirmación de entrega sin necesidad de iniciar sesión).
-- Se puede volver a correr sin duplicar nada.
USE LidessaDB;
GO

IF OBJECT_ID('dbo.SupplyOrderDeliveryToken', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.SupplyOrderDeliveryToken (
        Id                   BIGINT IDENTITY(1,1) PRIMARY KEY,
        OrderId              BIGINT NOT NULL CONSTRAINT FK_SupplyOrderDeliveryToken_Order REFERENCES dbo.SupplyOrder(Id) ON DELETE CASCADE,
        Token                NVARCHAR(100) NOT NULL,
        ExpiresAt            DATETIME2 NOT NULL,
        UsedAt               DATETIME2 NULL,
        CreatedAt            DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
    CREATE UNIQUE INDEX IX_SupplyOrderDeliveryToken_Token ON dbo.SupplyOrderDeliveryToken(Token);
END
GO
