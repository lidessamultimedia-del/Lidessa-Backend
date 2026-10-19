/* Llena la fila única de SiteSettings con los datos que el footer mostraba
   fijos antes de conectarse a la API. Solo toca los campos que sigan vacíos,
   así que no pisa lo que un admin ya haya guardado. */
USE LidessaDB;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.SiteSettings WHERE Id = 1)
    INSERT INTO dbo.SiteSettings (Id) VALUES (1);
GO

UPDATE dbo.SiteSettings SET
    Phone    = CASE WHEN Phone    = '' THEN N'+57 333 237 1006' ELSE Phone END,
    Email    = CASE WHEN Email    = '' THEN N'administrativo@lidessa.com' ELSE Email END,
    Address  = CASE WHEN Address  = '' THEN N'Cra. 71 #46-28, Laureles, Medellín, Antioquia' ELSE Address END,
    Schedule = CASE WHEN Schedule = '' THEN N'Lunes – Viernes: 7:00 a.m. – 4:30 p.m.' ELSE Schedule END
WHERE Id = 1;
GO
