/* ============================================================================
   LIDESSA — Esquema de base de datos (SQL Server / T-SQL)
   ----------------------------------------------------------------------------
   Refleja el modelo de datos REAL que ya existe hoy en el frontend (LMSContext,
   AuthContext, PQRSFContext, BlogContext, ServicesDataContext, SiteSettings),
   extraído directamente del código el 2026-09-01.

   Convenciones:
   - PK subrogada BIGINT IDENTITY en todas las tablas (rápida, simple para
     JOINs e índices). Se guarda además LegacyId (el id tipo 'c123...',
     't456...', string generado con Date.now() en el frontend actual) SOLO
     como columna de migración, para poder mapear los datos que ya existen
     en localStorage cuando se importen. Una vez migrado, el frontend nuevo
     usará el Id real (BIGINT) que devuelva la API — LegacyId no se vuelve
     a necesitar después de la migración inicial y se puede dropear.
   - Contraseñas: la tabla AppUser guarda PasswordHash, NUNCA texto plano.
     Hoy el frontend guarda password en texto plano en localStorage — eso NO
     se replica aquí a propósito. El backend debe hashear con algo como
     BCrypt/Argon2 antes de insertar.
   - Archivos/imágenes: hoy van como base64 embebido (avatar, foto de curso,
     hero de servicio, adjuntos de tarea/lección). Aquí se modelan como
     columnas de URL/ruta (StorageUrl), asumiendo que el backend los sube a
     disco/blob storage y no a una columna de base de datos. Ver plan de
     trabajo (docs/plan-backend-sept-oct.md) para la tarea de subida de
     archivos.
   - JSON: unos pocos campos anidados y muy heterogéneos que hoy nunca se
     consultan de forma relacional (tabs de un servicio, objetivos/módulos
     de un curso, opciones de una pregunta, respuestas de un intento) se
     guardan como NVARCHAR(MAX) con CHECK (ISJSON(...) = 1), usando las
     funciones JSON nativas de SQL Server en vez de crear 4-5 tablas hijas
     que nadie va a necesitar consultar por separado.
   ============================================================================ */

IF DB_ID('LidessaDB') IS NULL
BEGIN
    CREATE DATABASE LidessaDB;
END
GO

USE LidessaDB;
GO

/* ============================================================================
   1. USUARIOS Y AUTENTICACIÓN
   ============================================================================ */

CREATE TABLE dbo.AppUser (
    Id                   BIGINT IDENTITY(1,1) PRIMARY KEY,
    LegacyId             NVARCHAR(40) NULL,                 -- ids viejos tipo '1','t1','s1' (solo migración)
    Name                 NVARCHAR(150) NOT NULL,
    Email                NVARCHAR(256) NOT NULL,
    PasswordHash         NVARCHAR(256) NOT NULL,
    Role                 NVARCHAR(20)  NOT NULL
        CONSTRAINT CK_AppUser_Role CHECK (Role IN ('admin','profesor','estudiante')),
    Phone                NVARCHAR(30)  NOT NULL DEFAULT '',
    AvatarUrl             NVARCHAR(500) NULL,
    UnreadNotifications  INT NOT NULL DEFAULT 0,
    Active               BIT NOT NULL DEFAULT 1,
    CreatedAt            DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt            DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT UQ_AppUser_Email UNIQUE (Email)
);
GO

-- Único solo entre los valores reales (no NULL): un UNIQUE normal en SQL
-- Server solo admite una fila con NULL, y casi todas las filas nuevas creadas
-- por la API tienen LegacyId NULL (ver comentario arriba).
CREATE UNIQUE INDEX UQ_AppUser_LegacyId ON dbo.AppUser(LegacyId) WHERE LegacyId IS NOT NULL;
GO

-- Sesiones reales (reemplaza el sessionStorage actual). Un token por login.
CREATE TABLE dbo.UserSession (
    Id                   BIGINT IDENTITY(1,1) PRIMARY KEY,
    UserId               BIGINT NOT NULL CONSTRAINT FK_UserSession_User REFERENCES dbo.AppUser(Id) ON DELETE CASCADE,
    TokenHash            NVARCHAR(256) NOT NULL,            -- hash del refresh token, no el token en claro
    CreatedAt            DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    ExpiresAt            DATETIME2 NOT NULL,
    RevokedAt            DATETIME2 NULL
);
GO
CREATE INDEX IX_UserSession_UserId ON dbo.UserSession(UserId);

-- Códigos de recuperación de contraseña (hoy viven solo en memoria en el frontend)
CREATE TABLE dbo.PasswordResetCode (
    Id                   BIGINT IDENTITY(1,1) PRIMARY KEY,
    UserId               BIGINT NOT NULL CONSTRAINT FK_PasswordResetCode_User REFERENCES dbo.AppUser(Id) ON DELETE CASCADE,
    Code                 NVARCHAR(10) NOT NULL,
    ExpiresAt            DATETIME2 NOT NULL,
    UsedAt               DATETIME2 NULL,
    CreatedAt            DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
);
GO

CREATE TABLE dbo.DocumentType (
    Id                   BIGINT IDENTITY(1,1) PRIMARY KEY,
    Name                 NVARCHAR(80) NOT NULL UNIQUE       -- 'Cédula de ciudadanía', etc.
);
GO

-- Ficha extendida de profesor/estudiante (el admin NO tiene fila aquí, igual
-- que hoy no aparece en el `directory` del LMS, solo en AppUser).
CREATE TABLE dbo.PersonProfile (
    Id                   BIGINT IDENTITY(1,1) PRIMARY KEY,
    UserId               BIGINT NOT NULL UNIQUE
        CONSTRAINT FK_PersonProfile_User REFERENCES dbo.AppUser(Id) ON DELETE CASCADE,
    FirstName            NVARCHAR(100) NULL,
    LastName             NVARCHAR(100) NULL,
    DocumentTypeId       BIGINT NULL CONSTRAINT FK_PersonProfile_DocType REFERENCES dbo.DocumentType(Id),
    DocumentNumber       NVARCHAR(40) NULL,
    CourseInterest       NVARCHAR(200) NULL,                -- texto libre del registro público (curso de interés)
    JoinedDate           DATE NOT NULL DEFAULT CAST(SYSUTCDATETIME() AS DATE)
);
GO

/* ============================================================================
   2. CURSOS (LMS + CATÁLOGO CEET — es el mismo curso, unificado)
   ============================================================================ */

CREATE TABLE dbo.Course (
    Id                       BIGINT IDENTITY(1,1) PRIMARY KEY,
    LegacyId                 NVARCHAR(40) NULL UNIQUE,
    Name                     NVARCHAR(200) NOT NULL,
    ShortName                NVARCHAR(50)  NOT NULL DEFAULT '',
    Description              NVARCHAR(MAX) NOT NULL DEFAULT '',
    Category                 NVARCHAR(100) NOT NULL DEFAULT '',
    TeacherId                BIGINT NULL CONSTRAINT FK_Course_Teacher REFERENCES dbo.AppUser(Id),
    Format                   NVARCHAR(20) NOT NULL DEFAULT 'topics'
        CONSTRAINT CK_Course_Format CHECK (Format IN ('topics','weekly')),
    Published                BIT NOT NULL DEFAULT 0,
    Visible                  BIT NOT NULL DEFAULT 1,
    Listed                   BIT NOT NULL DEFAULT 0,          -- aparece en el catálogo público CEET
    StartDate                DATE NULL,
    EndDate                  DATE NULL,
    CompletionTrackingEnabled BIT NOT NULL DEFAULT 1,
    RequiresPassword         BIT NOT NULL DEFAULT 0,
    PasswordHash             NVARCHAR(256) NULL,              -- contraseña de acceso al curso, hasheada
    SelfEnrollment           BIT NOT NULL DEFAULT 0,
    GuestAccess               BIT NOT NULL DEFAULT 0,
    Capacity                 INT NULL,
    Color                    NVARCHAR(10) NULL,               -- hex, ej. '#005187'
    ImageUrl                 NVARCHAR(500) NULL,
    Duration                 NVARCHAR(50) NULL,                -- ej. '40 horas' (texto libre del catálogo)
    Modality                 NVARCHAR(20) NULL
        CONSTRAINT CK_Course_Modality CHECK (Modality IS NULL OR Modality IN ('Virtual','Presencial','Semipresencial')),
    Certified                BIT NOT NULL DEFAULT 0,
    Intro                    NVARCHAR(MAX) NULL,               -- solo cursos de catálogo CEET
    ObjectivesJson            NVARCHAR(MAX) NULL
        CONSTRAINT CK_Course_ObjectivesJson CHECK (ObjectivesJson IS NULL OR ISJSON(ObjectivesJson) = 1),
    ModulesJson               NVARCHAR(MAX) NULL
        CONSTRAINT CK_Course_ModulesJson CHECK (ModulesJson IS NULL OR ISJSON(ModulesJson) = 1),
    CreatedAt                DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt                DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
);
GO
CREATE INDEX IX_Course_TeacherId ON dbo.Course(TeacherId);

CREATE TABLE dbo.CourseEnrollment (
    CourseId             BIGINT NOT NULL CONSTRAINT FK_CourseEnrollment_Course REFERENCES dbo.Course(Id) ON DELETE CASCADE,
    StudentId            BIGINT NOT NULL CONSTRAINT FK_CourseEnrollment_Student REFERENCES dbo.AppUser(Id),
    EnrolledAt           DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_CourseEnrollment PRIMARY KEY (CourseId, StudentId)
);
GO

CREATE TABLE dbo.Topic (
    Id                   BIGINT IDENTITY(1,1) PRIMARY KEY,
    LegacyId             NVARCHAR(40) NULL UNIQUE,
    CourseId             BIGINT NOT NULL CONSTRAINT FK_Topic_Course REFERENCES dbo.Course(Id) ON DELETE CASCADE,
    Title                NVARCHAR(200) NOT NULL,
    SortOrder            INT NOT NULL DEFAULT 0
);
GO
CREATE INDEX IX_Topic_CourseId ON dbo.Topic(CourseId);

CREATE TABLE dbo.Lesson (
    Id                   BIGINT IDENTITY(1,1) PRIMARY KEY,
    LegacyId             NVARCHAR(40) NULL UNIQUE,
    CourseId             BIGINT NOT NULL CONSTRAINT FK_Lesson_Course REFERENCES dbo.Course(Id) ON DELETE CASCADE,
    TopicId              BIGINT NULL CONSTRAINT FK_Lesson_Topic REFERENCES dbo.Topic(Id),
    Title                NVARCHAR(200) NOT NULL,
    Content              NVARCHAR(MAX) NOT NULL DEFAULT '',
    SortOrder            INT NOT NULL DEFAULT 0,
    PublishAt            DATE NULL,
    AttachmentFileName   NVARCHAR(260) NULL,
    AttachmentUrl        NVARCHAR(500) NULL,
    AttachmentSizeBytes  BIGINT NULL
);
GO
CREATE INDEX IX_Lesson_CourseId ON dbo.Lesson(CourseId);
CREATE INDEX IX_Lesson_TopicId ON dbo.Lesson(TopicId);

/* ============================================================================
   3. TAREAS
   ============================================================================ */

CREATE TABLE dbo.Assignment (
    Id                   BIGINT IDENTITY(1,1) PRIMARY KEY,
    LegacyId             NVARCHAR(40) NULL UNIQUE,
    CourseId             BIGINT NOT NULL CONSTRAINT FK_Assignment_Course REFERENCES dbo.Course(Id) ON DELETE CASCADE,
    TopicId              BIGINT NULL CONSTRAINT FK_Assignment_Topic REFERENCES dbo.Topic(Id),
    Title                NVARCHAR(200) NOT NULL,
    Description          NVARCHAR(MAX) NOT NULL DEFAULT '',
    DueDate              DATETIME2 NULL,
    MaxScore             DECIMAL(4,1) NOT NULL DEFAULT 10.0,
    PublishAt            DATE NULL,
    AttachmentFileName   NVARCHAR(260) NULL,
    AttachmentUrl        NVARCHAR(500) NULL,
    AttachmentSizeBytes  BIGINT NULL
);
GO
CREATE INDEX IX_Assignment_CourseId ON dbo.Assignment(CourseId);

-- Vacío = para todo el curso. Con filas = solo para esos estudiantes.
CREATE TABLE dbo.AssignmentAssignee (
    AssignmentId         BIGINT NOT NULL CONSTRAINT FK_AssignmentAssignee_Assignment REFERENCES dbo.Assignment(Id) ON DELETE CASCADE,
    StudentId            BIGINT NOT NULL CONSTRAINT FK_AssignmentAssignee_Student REFERENCES dbo.AppUser(Id),
    CONSTRAINT PK_AssignmentAssignee PRIMARY KEY (AssignmentId, StudentId)
);
GO

CREATE TABLE dbo.Submission (
    Id                   BIGINT IDENTITY(1,1) PRIMARY KEY,
    LegacyId             NVARCHAR(40) NULL UNIQUE,
    AssignmentId         BIGINT NOT NULL CONSTRAINT FK_Submission_Assignment REFERENCES dbo.Assignment(Id) ON DELETE CASCADE,
    StudentId            BIGINT NOT NULL CONSTRAINT FK_Submission_Student REFERENCES dbo.AppUser(Id),
    AttachmentFileName   NVARCHAR(260) NULL,
    AttachmentUrl        NVARCHAR(500) NULL,
    AttachmentSizeBytes  BIGINT NULL,
    TextResponse         NVARCHAR(MAX) NOT NULL DEFAULT '',
    Notes                NVARCHAR(MAX) NOT NULL DEFAULT '',
    Status               NVARCHAR(20) NOT NULL DEFAULT 'draft'
        CONSTRAINT CK_Submission_Status CHECK (Status IN ('draft','submitted','graded')),
    SubmittedAt          DATETIME2 NULL,
    Grade                DECIMAL(4,1) NULL CONSTRAINT CK_Submission_Grade CHECK (Grade IS NULL OR Grade BETWEEN 0 AND 10),
    Feedback             NVARCHAR(MAX) NOT NULL DEFAULT '',
    GradedAt             DATETIME2 NULL,
    RetryAllowed         BIT NOT NULL DEFAULT 0,
    Seen                 BIT NOT NULL DEFAULT 0,
    CONSTRAINT UQ_Submission_Assignment_Student UNIQUE (AssignmentId, StudentId)
);
GO
CREATE INDEX IX_Submission_StudentId ON dbo.Submission(StudentId);

/* ============================================================================
   4. EXÁMENES (QUIZZES)
   ============================================================================ */

CREATE TABLE dbo.Quiz (
    Id                   BIGINT IDENTITY(1,1) PRIMARY KEY,
    LegacyId             NVARCHAR(40) NULL UNIQUE,
    CourseId             BIGINT NOT NULL CONSTRAINT FK_Quiz_Course REFERENCES dbo.Course(Id) ON DELETE CASCADE,
    TopicId              BIGINT NULL CONSTRAINT FK_Quiz_Topic REFERENCES dbo.Topic(Id),
    Title                NVARCHAR(200) NOT NULL,
    Description          NVARCHAR(MAX) NOT NULL DEFAULT '',
    DueDate              DATETIME2 NULL,
    PublishAt            DATE NULL,
    TimeLimitMinutes     INT NULL,
    SortOrder            INT NOT NULL DEFAULT 0
);
GO
CREATE INDEX IX_Quiz_CourseId ON dbo.Quiz(CourseId);

CREATE TABLE dbo.QuizQuestion (
    Id                   BIGINT IDENTITY(1,1) PRIMARY KEY,
    LegacyId             NVARCHAR(40) NULL UNIQUE,
    QuizId               BIGINT NOT NULL CONSTRAINT FK_QuizQuestion_Quiz REFERENCES dbo.Quiz(Id) ON DELETE CASCADE,
    SortOrder            INT NOT NULL DEFAULT 0,
    QuestionType         NVARCHAR(20) NOT NULL DEFAULT 'multiple'
        CONSTRAINT CK_QuizQuestion_Type CHECK (QuestionType IN ('multiple','open')),
    QuestionText         NVARCHAR(MAX) NOT NULL,
    OptionsJson          NVARCHAR(MAX) NULL                 -- array de strings, solo si QuestionType='multiple'
        CONSTRAINT CK_QuizQuestion_OptionsJson CHECK (OptionsJson IS NULL OR ISJSON(OptionsJson) = 1),
    CorrectIndex         INT NULL                           -- índice dentro de OptionsJson; NULL si es 'open'
);
GO
CREATE INDEX IX_QuizQuestion_QuizId ON dbo.QuizQuestion(QuizId);

CREATE TABLE dbo.QuizAssignee (
    QuizId               BIGINT NOT NULL CONSTRAINT FK_QuizAssignee_Quiz REFERENCES dbo.Quiz(Id) ON DELETE CASCADE,
    StudentId            BIGINT NOT NULL CONSTRAINT FK_QuizAssignee_Student REFERENCES dbo.AppUser(Id),
    CONSTRAINT PK_QuizAssignee PRIMARY KEY (QuizId, StudentId)
);
GO

CREATE TABLE dbo.QuizAttempt (
    Id                   BIGINT IDENTITY(1,1) PRIMARY KEY,
    LegacyId             NVARCHAR(40) NULL UNIQUE,
    QuizId               BIGINT NOT NULL CONSTRAINT FK_QuizAttempt_Quiz REFERENCES dbo.Quiz(Id) ON DELETE CASCADE,
    StudentId            BIGINT NOT NULL CONSTRAINT FK_QuizAttempt_Student REFERENCES dbo.AppUser(Id),
    AnswersJson          NVARCHAR(MAX) NOT NULL              -- array alineado por índice con QuizQuestion.SortOrder
        CONSTRAINT CK_QuizAttempt_AnswersJson CHECK (ISJSON(AnswersJson) = 1),
    Score                DECIMAL(4,1) NOT NULL CONSTRAINT CK_QuizAttempt_Score CHECK (Score BETWEEN 0 AND 10),
    Feedback             NVARCHAR(MAX) NOT NULL DEFAULT '',
    Reviewed              BIT NOT NULL DEFAULT 0,
    RetryAllowed          BIT NOT NULL DEFAULT 0,
    Seen                  BIT NOT NULL DEFAULT 0,
    SubmittedAt           DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT UQ_QuizAttempt_Quiz_Student UNIQUE (QuizId, StudentId)
    -- NOTA: hoy el frontend permite un solo intento guardado por estudiante/quiz
    -- (se sobreescribe con reintento). Si más adelante quieren *historial* de
    -- intentos, quitar este UNIQUE y agregar AttemptNumber.
);
GO
CREATE INDEX IX_QuizAttempt_StudentId ON dbo.QuizAttempt(StudentId);

CREATE TABLE dbo.LessonProgress (
    StudentId            BIGINT NOT NULL CONSTRAINT FK_LessonProgress_Student REFERENCES dbo.AppUser(Id),
    CourseId             BIGINT NOT NULL CONSTRAINT FK_LessonProgress_Course REFERENCES dbo.Course(Id) ON DELETE CASCADE,
    LessonId             BIGINT NOT NULL CONSTRAINT FK_LessonProgress_Lesson REFERENCES dbo.Lesson(Id),
    CompletedAt          DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_LessonProgress PRIMARY KEY (StudentId, LessonId)
);
GO

/* ============================================================================
   5. MENSAJERÍA Y CERTIFICACIONES
   ============================================================================ */

CREATE TABLE dbo.Message (
    Id                   BIGINT IDENTITY(1,1) PRIMARY KEY,
    LegacyId             NVARCHAR(40) NULL UNIQUE,
    CourseId             BIGINT NOT NULL CONSTRAINT FK_Message_Course REFERENCES dbo.Course(Id) ON DELETE CASCADE,
    FromUserId           BIGINT NOT NULL CONSTRAINT FK_Message_From REFERENCES dbo.AppUser(Id),
    ToUserId             BIGINT NOT NULL CONSTRAINT FK_Message_To REFERENCES dbo.AppUser(Id),
    Body                 NVARCHAR(MAX) NOT NULL,
    CreatedAt            DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    IsRead               BIT NOT NULL DEFAULT 0
);
GO
CREATE INDEX IX_Message_Thread ON dbo.Message(CourseId, FromUserId, ToUserId);
CREATE INDEX IX_Message_ToUserId ON dbo.Message(ToUserId);

CREATE TABLE dbo.Certification (
    Id                   BIGINT IDENTITY(1,1) PRIMARY KEY,
    LegacyId             NVARCHAR(40) NULL UNIQUE,
    StudentId            BIGINT NOT NULL CONSTRAINT FK_Certification_Student REFERENCES dbo.AppUser(Id),
    CourseId             BIGINT NOT NULL CONSTRAINT FK_Certification_Course REFERENCES dbo.Course(Id) ON DELETE CASCADE,
    MarkedAt             DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT UQ_Certification_Student_Course UNIQUE (StudentId, CourseId)
);
GO

/* ============================================================================
   6. BLOG / CONVERGE
   ============================================================================ */

CREATE TABLE dbo.BlogPost (
    Id                   BIGINT IDENTITY(1,1) PRIMARY KEY,
    LegacyId             NVARCHAR(40) NULL UNIQUE,
    Title                NVARCHAR(200) NOT NULL,
    Excerpt              NVARCHAR(MAX) NOT NULL DEFAULT '',
    PublishedOn          DATE NOT NULL DEFAULT CAST(SYSUTCDATETIME() AS DATE),
    ImageUrl             NVARCHAR(500) NOT NULL,
    Author               NVARCHAR(150) NOT NULL DEFAULT '',  -- firma visible en el post (puede ser alguien externo, no una cuenta)
    Phone                NVARCHAR(30) NOT NULL DEFAULT '',
    ExternalLink         NVARCHAR(500) NULL,
    CreatedByUserId       BIGINT NULL CONSTRAINT FK_BlogPost_CreatedBy REFERENCES dbo.AppUser(Id), -- admin logueado que publicó
    CreatedAt            DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
);
GO
CREATE INDEX IX_BlogPost_CreatedByUserId ON dbo.BlogPost(CreatedByUserId);

/* ============================================================================
   7. PQRSF
   ============================================================================ */

CREATE TABLE dbo.PqrsfTicket (
    Id                   BIGINT IDENTITY(1,1) PRIMARY KEY,
    TicketCode            NVARCHAR(30) NOT NULL UNIQUE,      -- 'PQRSF-2025-0041' (se sigue generando igual)
    TicketType             NVARCHAR(20) NOT NULL
        CONSTRAINT CK_PqrsfTicket_Type CHECK (TicketType IN ('Petición','Solicitud','Queja','Reclamo','Sugerencia','Felicitación')),
    FromName              NVARCHAR(150) NOT NULL DEFAULT 'Anónimo',
    Email                 NVARCHAR(256) NOT NULL DEFAULT '',
    Phone                 NVARCHAR(30) NOT NULL DEFAULT '',
    Subject               NVARCHAR(300) NOT NULL,
    MessageBody            NVARCHAR(MAX) NOT NULL DEFAULT '',
    TicketDate             DATE NOT NULL DEFAULT CAST(SYSUTCDATETIME() AS DATE),
    Status                NVARCHAR(20) NOT NULL DEFAULT 'Pendiente'
        CONSTRAINT CK_PqrsfTicket_Status CHECK (Status IN ('Pendiente','Revisando','Respondida')),
    Response               NVARCHAR(MAX) NULL,
    RespondedAt             DATETIME2 NULL,
    EmailSent              BIT NOT NULL DEFAULT 0,
    AccountId              BIGINT NULL CONSTRAINT FK_PqrsfTicket_Account REFERENCES dbo.AppUser(Id),
    IsRead                 BIT NOT NULL DEFAULT 0,
    CreatedAt              DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
);
GO
CREATE INDEX IX_PqrsfTicket_AccountId ON dbo.PqrsfTicket(AccountId);

/* ============================================================================
   8. SERVICIOS
   ============================================================================ */

CREATE TABLE dbo.ServiceCategory (
    Id                   BIGINT IDENTITY(1,1) PRIMARY KEY,
    Name                 NVARCHAR(150) NOT NULL UNIQUE,
    Active               BIT NOT NULL DEFAULT 1
);
GO

CREATE TABLE dbo.Service (
    Id                   BIGINT IDENTITY(1,1) PRIMARY KEY,
    Slug                 NVARCHAR(200) NOT NULL UNIQUE,
    CategoryId            BIGINT NOT NULL CONSTRAINT FK_Service_Category REFERENCES dbo.ServiceCategory(Id),
    Title                 NVARCHAR(200) NOT NULL,
    Description           NVARCHAR(MAX) NOT NULL DEFAULT '',
    HeroImageUrl          NVARCHAR(500) NULL,
    Active                BIT NOT NULL DEFAULT 1,
    Locked                BIT NOT NULL DEFAULT 0,            -- true = contenido protegido (los 16 servicios originales)
    TabsJson              NVARCHAR(MAX) NOT NULL             -- estructura completa de tabs/sections/bullets/checklist/structure
        CONSTRAINT CK_Service_TabsJson CHECK (ISJSON(TabsJson) = 1),
    CreatedAt             DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt             DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
);
GO
CREATE INDEX IX_Service_CategoryId ON dbo.Service(CategoryId);

/* ============================================================================
   9. CONFIGURACIÓN DEL SITIO (fila única)
   ============================================================================ */

CREATE TABLE dbo.SiteSettings (
    Id                   INT NOT NULL PRIMARY KEY DEFAULT 1 CONSTRAINT CK_SiteSettings_Singleton CHECK (Id = 1),
    Phone                NVARCHAR(30) NOT NULL DEFAULT '',
    Email                NVARCHAR(256) NOT NULL DEFAULT '',
    Address              NVARCHAR(300) NOT NULL DEFAULT '',
    Schedule             NVARCHAR(200) NOT NULL DEFAULT ''
);
GO
INSERT INTO dbo.SiteSettings (Id) VALUES (1);
GO
