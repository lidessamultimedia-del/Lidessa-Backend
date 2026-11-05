-- Para bases ya creadas con una versión anterior de schema.sql.
-- 1. Habilita el rol 'cliente' (cuentas de la tienda de Suministros).
-- 2. Crea las tablas de Suministros: productos, pedidos, líneas de pedido e
--    historial de estados (calcadas de SuppliesContext en el frontend).
-- 3. Carga los 3 productos de ejemplo que hoy trae el frontend (suppliesSeed.js).
-- Se puede volver a correr sin duplicar nada.
USE LidessaDB;
GO

/* 1. Rol 'cliente' ------------------------------------------------------- */
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_AppUser_Role')
    ALTER TABLE dbo.AppUser DROP CONSTRAINT CK_AppUser_Role;
GO
ALTER TABLE dbo.AppUser ADD CONSTRAINT CK_AppUser_Role
    CHECK (Role IN ('admin','profesor','estudiante','cliente'));
GO

/* 2. Tablas -------------------------------------------------------------- */
IF OBJECT_ID('dbo.SupplyProduct', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.SupplyProduct (
        Id                   BIGINT IDENTITY(1,1) PRIMARY KEY,
        Name                 NVARCHAR(200) NOT NULL,
        Description          NVARCHAR(MAX) NOT NULL DEFAULT '',
        Price                DECIMAL(14,2) NOT NULL CONSTRAINT CK_SupplyProduct_Price CHECK (Price >= 0),
        ImageUrl             NVARCHAR(500) NULL,
        Stock                INT NOT NULL DEFAULT 0 CONSTRAINT CK_SupplyProduct_Stock CHECK (Stock >= 0),
        Active               BIT NOT NULL DEFAULT 1,
        CreatedAt            DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        UpdatedAt            DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
END
GO

IF OBJECT_ID('dbo.SupplyOrder', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.SupplyOrder (
        Id                     BIGINT IDENTITY(1,1) PRIMARY KEY,
        ClientId               BIGINT NULL CONSTRAINT FK_SupplyOrder_Client REFERENCES dbo.AppUser(Id) ON DELETE SET NULL,
        CustomerName           NVARCHAR(150) NOT NULL,
        CustomerPhone          NVARCHAR(30)  NOT NULL,
        CustomerEmail          NVARCHAR(256) NOT NULL DEFAULT '',
        Address                NVARCHAR(300) NOT NULL,
        City                   NVARCHAR(100) NOT NULL DEFAULT '',
        Notes                  NVARCHAR(MAX) NOT NULL DEFAULT '',
        Total                  DECIMAL(14,2) NOT NULL CONSTRAINT CK_SupplyOrder_Total CHECK (Total >= 0),
        ProofImageUrl          NVARCHAR(500) NULL,
        Status                 NVARCHAR(20) NOT NULL DEFAULT 'pending'
            CONSTRAINT CK_SupplyOrder_Status CHECK (Status IN ('pending','confirmed','shipped','completed','rejected')),
        StatusUpdatedAt        DATETIME2 NULL,
        PaymentNoteAmount      DECIMAL(14,2) NULL,
        PaymentNoteMessage     NVARCHAR(MAX) NULL,
        PaymentNoteDate        DATETIME2 NULL,
        ShipmentCarrier        NVARCHAR(100) NULL,
        ShipmentTrackingNumber NVARCHAR(100) NULL,
        ShipmentPhotoUrl       NVARCHAR(500) NULL,
        ShippedAt              DATETIME2 NULL,
        RejectionReason        NVARCHAR(MAX) NULL,
        RefundProofUrl         NVARCHAR(500) NULL,
        CreatedAt              DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        UpdatedAt              DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
    CREATE INDEX IX_SupplyOrder_ClientId ON dbo.SupplyOrder(ClientId);
    CREATE INDEX IX_SupplyOrder_Status ON dbo.SupplyOrder(Status);
END
GO

IF OBJECT_ID('dbo.SupplyOrderItem', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.SupplyOrderItem (
        Id                   BIGINT IDENTITY(1,1) PRIMARY KEY,
        OrderId              BIGINT NOT NULL CONSTRAINT FK_SupplyOrderItem_Order REFERENCES dbo.SupplyOrder(Id) ON DELETE CASCADE,
        ProductId            BIGINT NULL CONSTRAINT FK_SupplyOrderItem_Product REFERENCES dbo.SupplyProduct(Id) ON DELETE SET NULL,
        ProductName          NVARCHAR(200) NOT NULL,
        UnitPrice            DECIMAL(14,2) NOT NULL CONSTRAINT CK_SupplyOrderItem_UnitPrice CHECK (UnitPrice >= 0),
        Quantity             INT NOT NULL CONSTRAINT CK_SupplyOrderItem_Quantity CHECK (Quantity > 0)
    );
    CREATE INDEX IX_SupplyOrderItem_OrderId ON dbo.SupplyOrderItem(OrderId);
    CREATE INDEX IX_SupplyOrderItem_ProductId ON dbo.SupplyOrderItem(ProductId);
END
GO

IF OBJECT_ID('dbo.SupplyOrderStatusChange', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.SupplyOrderStatusChange (
        Id                   BIGINT IDENTITY(1,1) PRIMARY KEY,
        OrderId              BIGINT NOT NULL CONSTRAINT FK_SupplyOrderStatusChange_Order REFERENCES dbo.SupplyOrder(Id) ON DELETE CASCADE,
        Status               NVARCHAR(20) NOT NULL
            CONSTRAINT CK_SupplyOrderStatusChange_Status CHECK (Status IN ('pending','confirmed','shipped','completed','rejected')),
        ChangedAt            DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
    CREATE INDEX IX_SupplyOrderStatusChange_OrderId ON dbo.SupplyOrderStatusChange(OrderId);
END
GO

/* 3. Productos de ejemplo (los mismos de suppliesSeed.js) --------------- */
IF NOT EXISTS (SELECT 1 FROM dbo.SupplyProduct WHERE Name = N'Botiquín tipo A certificado')
    INSERT INTO dbo.SupplyProduct (Name, Description, Price, ImageUrl, Stock, Active)
    VALUES (N'Botiquín tipo A certificado', N'Botiquín de primeros auxilios completo, conforme a la Resolución 0705 de 2007.', 185000, N'/assets/insumos.png', 12, 1);
IF NOT EXISTS (SELECT 1 FROM dbo.SupplyProduct WHERE Name = N'Casco de protección craneal')
    INSERT INTO dbo.SupplyProduct (Name, Description, Price, ImageUrl, Stock, Active)
    VALUES (N'Casco de protección craneal', N'Casco certificado NTC 3610 para zonas con riesgo de golpes o caída de objetos.', 65000, N'/assets/insumos.png', 30, 1);
IF NOT EXISTS (SELECT 1 FROM dbo.SupplyProduct WHERE Name = N'Gafas de protección visual')
    INSERT INTO dbo.SupplyProduct (Name, Description, Price, ImageUrl, Stock, Active)
    VALUES (N'Gafas de protección visual', N'Gafas certificadas ANSI Z87.1, antiempañantes.', 22000, N'/assets/insumos.png', 50, 1);
GO
