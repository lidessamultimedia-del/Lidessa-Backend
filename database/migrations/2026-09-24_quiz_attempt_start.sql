-- Para bases ya creadas con una versión anterior de schema.sql.
-- Agrega la tabla QuizAttemptStart (tiempo límite de exámenes controlado en el servidor).
USE LidessaDB;
GO

IF OBJECT_ID('dbo.QuizAttemptStart', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.QuizAttemptStart (
        QuizId               BIGINT NOT NULL CONSTRAINT FK_QuizAttemptStart_Quiz REFERENCES dbo.Quiz(Id) ON DELETE CASCADE,
        StudentId            BIGINT NOT NULL CONSTRAINT FK_QuizAttemptStart_Student REFERENCES dbo.AppUser(Id),
        StartedAt            DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_QuizAttemptStart PRIMARY KEY (QuizId, StudentId)
    );
END
GO
