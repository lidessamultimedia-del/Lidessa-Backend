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

-- Tokens de invitación: el admin crea la cuenta de profesor/estudiante sin
-- contraseña, y la persona la activa (elige su propia contraseña) con este token.
CREATE TABLE dbo.AccountInviteToken (
    Id                   BIGINT IDENTITY(1,1) PRIMARY KEY,
    UserId               BIGINT NOT NULL CONSTRAINT FK_AccountInviteToken_User REFERENCES dbo.AppUser(Id) ON DELETE CASCADE,
    Token                NVARCHAR(100) NOT NULL,
    ExpiresAt            DATETIME2 NOT NULL,
    UsedAt               DATETIME2 NULL,
    CreatedAt            DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
);
GO
CREATE UNIQUE INDEX UX_AccountInviteToken_Token ON dbo.AccountInviteToken(Token);
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
    LegacyId                 NVARCHAR(40) NULL,
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
CREATE UNIQUE INDEX UQ_Course_LegacyId ON dbo.Course(LegacyId) WHERE LegacyId IS NOT NULL;
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
    LegacyId             NVARCHAR(40) NULL,
    CourseId             BIGINT NOT NULL CONSTRAINT FK_Topic_Course REFERENCES dbo.Course(Id) ON DELETE CASCADE,
    Title                NVARCHAR(200) NOT NULL,
    SortOrder            INT NOT NULL DEFAULT 0
);
GO
CREATE UNIQUE INDEX UQ_Topic_LegacyId ON dbo.Topic(LegacyId) WHERE LegacyId IS NOT NULL;
GO

CREATE INDEX IX_Topic_CourseId ON dbo.Topic(CourseId);

CREATE TABLE dbo.Lesson (
    Id                   BIGINT IDENTITY(1,1) PRIMARY KEY,
    LegacyId             NVARCHAR(40) NULL,
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
CREATE UNIQUE INDEX UQ_Lesson_LegacyId ON dbo.Lesson(LegacyId) WHERE LegacyId IS NOT NULL;
GO

CREATE INDEX IX_Lesson_CourseId ON dbo.Lesson(CourseId);
CREATE INDEX IX_Lesson_TopicId ON dbo.Lesson(TopicId);

/* ============================================================================
   3. TAREAS
   ============================================================================ */

CREATE TABLE dbo.Assignment (
    Id                   BIGINT IDENTITY(1,1) PRIMARY KEY,
    LegacyId             NVARCHAR(40) NULL,
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
CREATE UNIQUE INDEX UQ_Assignment_LegacyId ON dbo.Assignment(LegacyId) WHERE LegacyId IS NOT NULL;
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
    LegacyId             NVARCHAR(40) NULL,
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
CREATE UNIQUE INDEX UQ_Submission_LegacyId ON dbo.Submission(LegacyId) WHERE LegacyId IS NOT NULL;
GO

CREATE INDEX IX_Submission_StudentId ON dbo.Submission(StudentId);

/* ============================================================================
   4. EXÁMENES (QUIZZES)
   ============================================================================ */

CREATE TABLE dbo.Quiz (
    Id                   BIGINT IDENTITY(1,1) PRIMARY KEY,
    LegacyId             NVARCHAR(40) NULL,
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
CREATE UNIQUE INDEX UQ_Quiz_LegacyId ON dbo.Quiz(LegacyId) WHERE LegacyId IS NOT NULL;
GO

CREATE INDEX IX_Quiz_CourseId ON dbo.Quiz(CourseId);

CREATE TABLE dbo.QuizQuestion (
    Id                   BIGINT IDENTITY(1,1) PRIMARY KEY,
    LegacyId             NVARCHAR(40) NULL,
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
CREATE UNIQUE INDEX UQ_QuizQuestion_LegacyId ON dbo.QuizQuestion(LegacyId) WHERE LegacyId IS NOT NULL;
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
    LegacyId             NVARCHAR(40) NULL,
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
CREATE UNIQUE INDEX UQ_QuizAttempt_LegacyId ON dbo.QuizAttempt(LegacyId) WHERE LegacyId IS NOT NULL;
GO

CREATE INDEX IX_QuizAttempt_StudentId ON dbo.QuizAttempt(StudentId);

-- Momento en que el estudiante abrió un examen con tiempo límite. Lo usa el
-- backend para rechazar/anular entregas fuera de tiempo (el cronómetro del
-- navegador no es confiable). Se borra al entregar, así un reintento
-- autorizado arranca con el tiempo completo.
CREATE TABLE dbo.QuizAttemptStart (
    QuizId               BIGINT NOT NULL CONSTRAINT FK_QuizAttemptStart_Quiz REFERENCES dbo.Quiz(Id) ON DELETE CASCADE,
    StudentId            BIGINT NOT NULL CONSTRAINT FK_QuizAttemptStart_Student REFERENCES dbo.AppUser(Id),
    StartedAt            DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_QuizAttemptStart PRIMARY KEY (QuizId, StudentId)
);
GO

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
    LegacyId             NVARCHAR(40) NULL,
    CourseId             BIGINT NOT NULL CONSTRAINT FK_Message_Course REFERENCES dbo.Course(Id) ON DELETE CASCADE,
    FromUserId           BIGINT NOT NULL CONSTRAINT FK_Message_From REFERENCES dbo.AppUser(Id),
    ToUserId             BIGINT NOT NULL CONSTRAINT FK_Message_To REFERENCES dbo.AppUser(Id),
    Body                 NVARCHAR(MAX) NOT NULL,
    CreatedAt            DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    IsRead               BIT NOT NULL DEFAULT 0
);
GO
CREATE UNIQUE INDEX UQ_Message_LegacyId ON dbo.Message(LegacyId) WHERE LegacyId IS NOT NULL;
GO

CREATE INDEX IX_Message_Thread ON dbo.Message(CourseId, FromUserId, ToUserId);
CREATE INDEX IX_Message_ToUserId ON dbo.Message(ToUserId);

CREATE TABLE dbo.Certification (
    Id                   BIGINT IDENTITY(1,1) PRIMARY KEY,
    LegacyId             NVARCHAR(40) NULL,
    StudentId            BIGINT NOT NULL CONSTRAINT FK_Certification_Student REFERENCES dbo.AppUser(Id),
    CourseId             BIGINT NOT NULL CONSTRAINT FK_Certification_Course REFERENCES dbo.Course(Id) ON DELETE CASCADE,
    MarkedAt             DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT UQ_Certification_Student_Course UNIQUE (StudentId, CourseId)
);
GO
CREATE UNIQUE INDEX UQ_Certification_LegacyId ON dbo.Certification(LegacyId) WHERE LegacyId IS NOT NULL;
GO


/* ============================================================================
   6. BLOG / CONVERGE
   ============================================================================ */

CREATE TABLE dbo.BlogPost (
    Id                   BIGINT IDENTITY(1,1) PRIMARY KEY,
    LegacyId             NVARCHAR(40) NULL,
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
CREATE UNIQUE INDEX UQ_BlogPost_LegacyId ON dbo.BlogPost(LegacyId) WHERE LegacyId IS NOT NULL;
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

-- Siembra los 16 servicios originales del sitio y sus 5 categorías
-- (ver database/migrations/2026-10-29_services_seed.sql), para que una base
-- nueva creada desde este archivo ya los tenga, igual que las demás
-- migraciones ya incorporadas más abajo.
IF NOT EXISTS (SELECT 1 FROM dbo.ServiceCategory WHERE Name = N'Gestión empresarial')
    INSERT INTO dbo.ServiceCategory (Name, Active) VALUES (N'Gestión empresarial', 1);
IF NOT EXISTS (SELECT 1 FROM dbo.ServiceCategory WHERE Name = N'Instituciones educativas')
    INSERT INTO dbo.ServiceCategory (Name, Active) VALUES (N'Instituciones educativas', 1);
IF NOT EXISTS (SELECT 1 FROM dbo.ServiceCategory WHERE Name = N'Educación y formación')
    INSERT INTO dbo.ServiceCategory (Name, Active) VALUES (N'Educación y formación', 1);
IF NOT EXISTS (SELECT 1 FROM dbo.ServiceCategory WHERE Name = N'Propiedades horizontales')
    INSERT INTO dbo.ServiceCategory (Name, Active) VALUES (N'Propiedades horizontales', 1);
IF NOT EXISTS (SELECT 1 FROM dbo.ServiceCategory WHERE Name = N'Supervisión e interventoría')
    INSERT INTO dbo.ServiceCategory (Name, Active) VALUES (N'Supervisión e interventoría', 1);
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Service WHERE Slug = N'gestion-de-calidad')
    INSERT INTO dbo.Service (Slug, CategoryId, Title, Description, HeroImageUrl, Active, Locked, TabsJson)
    SELECT N'gestion-de-calidad', Id, N'Gestión de Calidad', N'En Lidessa enfocamos nuestros esfuerzos para garantizar que su empresa cumpla con los más altos estándares de calidad. Implementando sistemas de gestión de calidad personalizados que se alineen con sus objetivos y aseguren el cumplimiento normativo. Nuestra meta es mejorar la eficiencia operativa, reducir errores y aumentar la satisfacción de sus clientes, promoviendo una cultura organizacional orientada hacia la excelencia.', N'/assets/Ips.jpg', 1, 1, N'[{"label":"ISO","sections":[{"heading":"¿Qué es ISO?","text":"ISO (Organización Internacional de Normalización) es una entidad independiente y no gubernamental que desarrolla y publica normas internacionales. Estas normas ayudan a asegurar la calidad, seguridad y eficiencia de productos, servicios y sistemas a nivel global."},{"heading":"¿Cómo contribuye Lidessa al cumplimiento de la Norma?","text":"En Lidessa, ofrecemos servicios de consultoría y soporte para la implementación y mantenimiento de sistemas de gestión basados en normas ISO. Trabajamos con usted para adaptar estas normas a las necesidades específicas de su empresa, garantizando que cumplan con los requisitos internacionales y mejoren continuamente la calidad y eficiencia de sus procesos."},{"heading":"Beneficios para su empresa:","bullets":[{"label":"Certificación Internacional:","text":"Obtenga certificaciones ISO que validen la calidad y eficiencia de sus operaciones."},{"label":"Mejora Continua:","text":"Implemente prácticas que promuevan la mejora continua y la satisfacción del cliente."},{"label":"Ventaja Competitiva:","text":"Destaque en el mercado al cumplir con estándares internacionales reconocidos."},{"label":"Ventaja en Licitaciones:","text":"Mejoras en los puntajes de licitación empresarial."}]}]},{"label":"NTC","sections":[{"heading":"¿Qué son las NTC?","text":"NTC (Normas Técnicas Colombianas) son normas elaboradas por la Oficina de Normas de Colombia para establecer especificaciones y criterios técnicos que aseguren la calidad y seguridad de productos y servicios en el país."},{"heading":"¿Cómo contribuye Lidessa al cumplimiento de la Norma?","text":"Lidessa facilita la integración de las NTC en su empresa, ayudándole a cumplir con las regulaciones locales y asegurar que sus productos y servicios cumplan con los estándares nacionales. Nuestro equipo le guía en la implementación de estas normas, lo que le permite adaptarse a las exigencias del mercado colombiano."},{"heading":"Beneficios para su empresa:","bullets":[{"label":"Cumplimiento Regulatorio:","text":"Asegure que sus productos y servicios cumplan con las regulaciones locales."},{"label":"Acceso a Mercados Locales:","text":"Mejore su acceso y aceptación en el mercado colombiano."},{"label":"Calidad Asegurada:","text":"Asegure la calidad y seguridad de sus productos y servicios conforme a estándares nacionales."}]}]},{"label":"NTS","sections":[{"heading":"¿Qué son las NTS?","text":"Las NTS (Normas Técnicas Sectoriales) son normas específicas para diferentes sectores de la industria, desarrolladas para abordar requisitos y desafíos particulares en áreas como la construcción, alimentos, turismo y más."},{"heading":"¿Cómo contribuye Lidessa al cumplimiento de la Norma?","text":"En Lidessa, proporcionamos asesoría especializada para la implementación de NTS en su sector. Nuestro objetivo es ayudarle a cumplir con las normas técnicas sectoriales relevantes, asegurando que sus procesos y productos se ajusten a las especificaciones requeridas para su industria."},{"heading":"Beneficios para su empresa:","bullets":[{"label":"Cumplimiento Específico:","text":"Adapte sus operaciones a las normas técnicas específicas de su sector."},{"label":"Optimización del Proceso:","text":"Mejore la eficiencia y efectividad de sus procesos internos."},{"label":"Conformidad Sectorial:","text":"Cumpla con los requisitos y expectativas del sector en el que opera."}]}]}]'
    FROM dbo.ServiceCategory WHERE Name = N'Gestión empresarial';
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Service WHERE Slug = N'desarrollo-empresarial')
    INSERT INTO dbo.Service (Slug, CategoryId, Title, Description, HeroImageUrl, Active, Locked, TabsJson)
    SELECT N'desarrollo-empresarial', Id, N'Gestión de Desarrollo Empresarial', N'Diseñamos estrategias de crecimiento y transformación organizacional que fortalecen las capacidades internas de su empresa, optimizan recursos y potencian la competitividad en el mercado.', N'/assets/Servicio-cliente.jpg', 1, 1, N'[{"label":"RIT","title":"Reglamento Interno de Trabajo","intro":"Las empresas que tienen más de 5 trabajadores de carácter permanente si son comerciales, o más de diez (10) si son industriales, o más de veinte (20) si son agrícolas, ganaderas o forestales, necesitan un reglamento de trabajo, sin embargo, es recomendable que todas las empresas los tengan ya que esta es la carta de navegación para regular las relaciones laborales, además de otros aspectos importantes a considerar:","checklist":["Con la cultura actual y dinámica social y laboral, la tutela se ha convertido en un mecanismo de reclamación, los RIT bien implementados pueden prevenir tutelas por reintegro en casos de despido ilegal de sus trabajadores, así como el pago de todos los salarios y derechos dejados de percibir por ese trabajador desde que se despidió de forma ilegal, sumado a indemnización que contempla la Ley Laboral.","En el contexto de una relación laboral y los mismos niveles de informalidad que se manejan las mismas relaciones con los colaboradores, en el caso de dificultades o problema con un trabajador, el empleador se puede limitar a realizar una terminación de contrato, en muchos casos aunque exista una relación de peso que justifique un despido, al no contar con un reglamento Interno que imponga una sanción y un adecuado proceso de descargos, este despido aunque justificado, en la realidad se vuelve ilegal, lo que resulta en los riesgos de reintegro o indemnización.","Es importante tener en cuenta que el Ministerio de Trabajo puede imponer sanciones de hasta 5.000 Smlmv para las empresas que no cuenten con este documento. La sanción es graduable dependiendo de la gravedad de la falta, la reincidencia de la compañía y el tamaño de la misma."],"structure":{"title":"Estructura del Servicio","image":"/assets/Descripcion_Estructura.png","steps":["Diagnóstico de la compañía","Adecuación al marco legal","Reunión con gerencia, colaboradores y líderes de equipo","Proceso de aprobación","Publicación y optimización"]}},{"label":"Vinculación","sections":[{"heading":"Vinculación","text":"El proceso de vinculación inicia con un análisis detallado de los perfiles que la empresa requiere, seguido de una búsqueda exhaustiva en nuestro amplio banco de talentos. Luego, llevamos a cabo un proceso de selección riguroso, que incluye entrevistas, evaluaciones psicotécnicas y verificaciones de referencias, asegurando que los candidatos propuestos cumplan con los estándares exigidos."},{"heading":"De su interés","text":"En Colombia, la vinculación de empleados está regida por el Código Sustantivo del Trabajo y otras normativas laborales, como la Ley 789 de 2002 y la Ley 1562 de 2012. Estas leyes exigen que las empresas contraten de manera formal, cumpliendo con las obligaciones de seguridad social, riesgos laborales y garantizando condiciones dignas de trabajo. Las empresas que no cumplen con estas normativas pueden enfrentar sanciones como multas por parte del Ministerio del Trabajo, demandas laborales, y el riesgo de ser incluidas en listas negras que dificultan la contratación futura de personal. La falta de afiliación a la seguridad social o a riesgos laborales, por ejemplo, puede generar multas que varían entre 1 y 5.000 salarios mínimos mensuales legales vigentes, dependiendo de la gravedad del incumplimiento."},{"heading":"¿Cómo le ayuda Lidessa?","text":"Lidessa ofrece un servicio de vinculación integral que asegura que las empresas cumplan con todas las normativas laborales vigentes. Al gestionar el proceso de reclutamiento y selección de manera eficiente, Lidessa garantiza que los empleados contratados estén debidamente afiliados a los sistemas de seguridad social y riesgos laborales, reduciendo así el riesgo de sanciones. Además, la empresa ofrece asesoría continua para mantener la conformidad con las leyes laborales."}]},{"label":"Diseño de Estructura Interna","sections":[{"heading":"Diseño de Estructura Interna","text":"El buen funcionamiento de una organización depende en gran medida de su capacidad de clarificar y medir las acciones y funciones de sus colaboradores en relación a sus procesos. La estructura empresarial es la forma en que se organiza internamente una empresa. No hay una única estructura organizativa válida, sino diferentes tipos en función de los objetivos de la compañía. Se entiende por estructura empresarial la organización que se dota a una empresa para funcionar. El diseño organizacional implica conocer bien las cadenas de mando, puestos de trabajo, jerarquías y dependencias, así como los diversos departamentos en los que se ordena la compañía. Los responsables de diseñar la estructura organizativa cuentan en primer lugar con el organigrama de la empresa, pero no sólo eso."},{"heading":"La creación de la estructura organizacional de una empresa consta de dos pasos:","bullets":[{"text":"Analizar tareas, subtareas y definir metas de la organización."},{"text":"Llevar un control de la evaluación de objetivos."}]},{"heading":"Toda estructura organizacional cuenta con algunos componentes fundamentales que las distinguen de otras:","bullets":[{"label":"Cadena de Mando:","text":"Es la base de todo modelo organizacional. Se trata de una línea de autoridad que fluye desde la más alta dirección hasta los puestos más bajos. Esta cadena define a quién hay que dirigirse según el tema que se trate."},{"label":"Nivel de centralización:","text":"Que la organización esté centralizada o descentralizada contribuirá directamente en la velocidad del proceso de toma de decisiones. También tendrá un impacto en la percepción más o menos democrática de la forma de actuar."},{"label":"Margen de control:","text":"Es un elemento muy influido por el tamaño de la compañía y el nivel de centralización. Cuantos más empleados controle un mando directivo, mayor será su margen de control."},{"label":"Grado de especialización:","text":"Esta clave de la estructura organizacional aborda la división del trabajo. Dentro de una organización, las tareas se distribuyen en distintos niveles. Si los empleados cuentan con una especialización alta, estos serán expertos en su campo y serán más productivos."}]},{"heading":"Diseño estructural","text":"El diseño estructural se ocupa de la investigación metódica de la estabilidad, resistencia y rigidez de la estructura. El objetivo básico es crear una estructura que sea capaz de resistir todas las cargas aplicadas sin fallar durante su vida prevista. Algunos componentes básicos son:","bullets":[{"text":"Manuales de funciones."},{"text":"Llevar un control de la evaluación de objetivos."},{"text":"Herramientas de evaluación y desempeño."},{"text":"Estructuración funcional y de líneas de mando."},{"text":"Establecimiento de indicadores de gestión y operativos."},{"text":"Análisis del contexto interno y externo de la empresa."}]}]},{"label":"Manual de Funciones","sections":[{"heading":"Manual de Funciones","text":"El Manual de Funciones es una herramienta clave para definir claramente las responsabilidades, funciones y procesos dentro de una empresa. Lidessa ofrece la creación y actualización de manuales de funciones que alinean las operaciones con los objetivos estratégicos de la organización. Este servicio incluye la elaboración de un documento exhaustivo que detalla las funciones de cada puesto dentro de la organización, así como las competencias necesarias, los flujos de trabajo y los indicadores de desempeño. Este manual es fundamental para mejorar la eficiencia operativa, facilitar la formación del personal y asegurar la claridad en los roles y responsabilidades."},{"heading":"Lidessa le ofrece:","bullets":[{"label":"Diagnóstico organizacional:","text":"Análisis de la estructura actual de la empresa para identificar áreas de mejora."},{"label":"Definición de roles:","text":"Clarificación de funciones, responsabilidades y expectativas para cada puesto de trabajo."},{"label":"Optimización de procesos:","text":"Mejora en la asignación de tareas y en la coordinación interdepartamental."},{"label":"Herramienta de gestión:","text":"Un manual que se convierte en un referente para la gestión del talento y el desarrollo organizacional."}]},{"heading":"Recuerde:","boxed":true,"text":"El Manual de Funciones es un documento fundamental para el cumplimiento de varias normativas laborales en Colombia, incluyendo el Código Sustantivo del Trabajo y el Decreto 1072 de 2015. Este manual ayuda a formalizar y clarificar las responsabilidades de los empleados, facilitando la gestión de recursos humanos y asegurando que se cumplan los estándares de eficiencia y transparencia en las operaciones empresariales. La ausencia de un manual de funciones o la falta de claridad en los roles y responsabilidades puede llevar a conflictos laborales, sanciones por parte de entidades de inspección laboral, y demandas por incumplimientos contractuales. Las sanciones pueden incluir multas y, en casos extremos, la suspensión temporal de actividades hasta que se subsanen las deficiencias en la organización."}]},{"label":"PMIRS","sections":[{"heading":"Plan de Manejo Integral de Residuos Sólidos","text":"Los PMIRS o Planes de Manejo Integral de Residuos Sólidos, son planes estratégicos que buscan gestionar adecuadamente los residuos sólidos generados por la empresa, minimizando los riesgos para la salud de los trabajadores y el impacto ambiental. Estos programas incluyen la identificación, segregación, almacenamiento, transporte y disposición final de los residuos, asegurando que se cumplan con las normativas legales y se promueva un ambiente de trabajo seguro y saludable, reduciendo posibles accidentes, enfermedades ocupacionales, y contribuyendo a la sostenibilidad. Para el acompañamiento del Plan de Manejo Integral de Residuos Sólidos se desarrollan las siguientes actividades:","bullets":[{"text":"Identificar por medio de un diagnóstico inicial, cuál es el estado actual de los procedimientos encargados de la gestión integral de los residuos sólidos en la entidad, evaluando cada una de sus etapas. (Generación, movimiento interno de residuos, almacenamiento, transporte y disposición final)."},{"text":"Desarrollar estrategias de formación y educación para incentivar a la adecuada separación en la fuente de los residuos generados en la entidad, con el propósito de optimizar los residuos aprovechables, y así disminuir el volumen de residuos depositados en el centro de acopio. (campañas de sensibilización y educación ambiental)."},{"text":"Adoptar un plan de acción que incluya prevención, separación, almacenamiento, segregación, recolección, tratamiento y disposición final."}]}]},{"label":"RP","title":"Riesgo Psicosocial","sections":[{"boxed":true,"text":"Licencia SG-SST No S2017060095482 de 2017 y S2018060230500 de 2018, por medio de las cuales la secretaría de Salud del Departamento de Antioquia concede licencia para ofertar servicios de seguridad y salud en el trabajo a Nivel nacional a persona jurídica."},{"text":"El Ministerio de la Protección Social (ahora Ministerio de Trabajo) y la Universidad Javeriana desarrollaron los únicos formatos autorizados que cumplen con los criterios técnicos establecidos por la Resolución 2764 del 2022, para el uso en territorio colombiano. Hasta la fecha los cuestionarios de la batería de medición de riesgo psicosocial deben aplicarse utilizando lapicero y papel, de manera presencial. Bajo ninguna circunstancia puede realizarse la medición del riesgo psicosocial utilizando medios virtuales como páginas web, cuestionarios magnéticos ni otros métodos."},{"text":"Nuestra empresa se especializa en la identificación, evaluación y prevención de riesgos psicosociales en el trabajo. Entendemos que los factores psicosociales pueden afectar negativamente la salud mental y física de los trabajadores, así como la productividad y el bienestar de las organizaciones. Nuestros servicios abarcan la evaluación de los siguientes factores de riesgo psicosocial:","bullets":[{"label":"Factores intralaborales:","text":"demanda de trabajo, control y apoyo social en el trabajo, claridad de rol y expectativas, entre otros."},{"label":"Factores extralaborales:","text":"apoyo social fuera del trabajo, equilibrio entre la vida laboral y personal, entre otros."},{"label":"Estrés:","text":"identificamos las fuentes de estrés laboral y sus efectos en la salud y el bienestar."}]},{"heading":"Nuestros servicios incluyen:","bullets":[{"text":"Evaluación de los factores de riesgo psicosocial mediante baterías de instrumentos validados."},{"text":"Análisis de los resultados y recomendaciones para la implementación de medidas de prevención y control."},{"text":"Diseño y ejecución de programas de intervención para la promoción de la salud mental y el bienestar en el trabajo."}]},{"heading":"Importante","boxed":true,"text":"La multa que se le asignará, debido a que el Decreto 472 del 17 de marzo de 2015 estableció sanciones que inician desde los 50 salarios mínimos legales vigentes. Adicionalmente recuerde que la Resolución 1111 de 2017 establece los Estándares Mínimo de obligatorio cumplimiento en Seguridad y Salud en el Trabajo, con los cuales la empresa vela por el bienestar físico y mental del empleado. Recuerde que la única persona autorizada en Colombia para realizar la evaluación y generar diagnósticos para la prevención y el control de enfermedades generadas por los riesgos psicosociales, es un Psicólogo graduado como Especialista en Seguridad y Salud en el Trabajo."}]},{"label":"TD","title":"Tratamiento de Bases de datos","sections":[{"text":"La gestión de bases de datos en Colombia está regulada principalmente por la Ley 1581 de 2012 (Ley de Protección de Datos Personales) y su Decreto 1081 de 2015. Estas normativas imponen a las empresas la responsabilidad de garantizar la seguridad, confidencialidad y correcto uso de los datos personales que manejan. Para cumplir con estas obligaciones, es fundamental implementar medidas técnicas y organizativas que protejan adecuadamente la información sensible, evitando su uso indebido y asegurando la privacidad de los titulares de los datos."},{"text":"En Lidessa ofrecemos un servicio especializado en el diseño e implementación del marco legal para el tratamiento de datos personales, asegurando el cumplimiento de todas las normativas vigentes. Nuestro enfoque integral abarca desde la creación de políticas de tratamiento de datos hasta la gestión del riesgo asociado a los procesos de manejo de datos personales. Entendemos que cada empresa tiene necesidades específicas, por lo que diseñamos manuales personalizados que incluyen guías, instructivos y herramientas para el adecuado tratamiento de datos personales, conforme a la ley."},{"text":"Además, proporcionamos una caracterización detallada de los datos organizacionales, identificando los tipos de datos tratados y estableciendo mecanismos claros para su manejo seguro. También ofrecemos soporte en la definición de un Oficial de Protección de Datos Personales (OPD) y la conformación de un comité interno, integrado por diferentes dependencias de la organización, que toma decisiones estratégicas en relación con el tratamiento de datos. Asimismo, nos encargamos de realizar el registro ante la Superintendencia de Industria y Comercio (SIC) cuando es requerido."},{"text":"Con Lidessa, las empresas no solo aseguran el cumplimiento normativo, sino que también protegen su reputación corporativa, evitando sanciones costosas y garantizando que los derechos de los titulares de los datos sean respetados."},{"heading":"Le ofrecemos:","bullets":[{"label":"Diseño personalizado:","text":"Creación de bases de datos estructuradas según las necesidades y procesos específicos de la empresa, incluyendo manuales de tratamiento de datos y políticas alineadas con la normativa vigente."},{"label":"Mantenimiento continuo:","text":"Actualización y optimización de las bases de datos para asegurar su eficiencia y cumplimiento a largo plazo, adaptándose a los cambios regulatorios y tecnológicos."},{"label":"Integración de sistemas:","text":"Conexión de la base de datos con otros sistemas de la empresa para un flujo de información eficiente y seguro, asegurando que todos los procesos estén alineados y sean consistentes en el manejo de datos."},{"label":"Capacitación normativa:","text":"Formación especializada para el personal, enfocada en las leyes de protección de datos personales, para garantizar que todos en la organización comprendan y apliquen correctamente las normativas."},{"label":"Gestión del riesgo:","text":"Evaluación y mitigación de riesgos asociados al tratamiento de datos personales, asegurando que la empresa esté preparada para manejar cualquier eventualidad de manera efectiva."},{"label":"Registro ante la SIC:","text":"Asesoría y gestión del registro de bases de datos ante la Superintendencia de Industria y Comercio, cumpliendo con los requisitos legales."}]}]},{"label":"Planes de emergencia","title":"Planes de emergencia","sections":[{"text":"Un Plan de Emergencia es un conjunto de procedimientos y directrices diseñados para manejar situaciones imprevistas o crisis de manera efectiva. Este plan tiene como objetivo minimizar el impacto de emergencias, garantizar la seguridad de las personas y proteger los activos de la organización. Un plan de emergencia bien elaborado permite una respuesta organizada y eficiente ante eventos como incendios, desastres naturales, accidentes graves, fallos de infraestructura o cualquier otra situación de emergencia que pueda afectar a una organización o comunidad."},{"heading":"Le ofrecemos:","bullets":[{"label":"Identificación de Riesgos:","text":"Evaluación y análisis de los riesgos potenciales que podrían afectar a la organización o comunidad. Clasificación de riesgos según su probabilidad y severidad."},{"label":"Procedimientos de Evacuación:","text":"Diseño de rutas de evacuación claras y señalizadas. Establecimiento de puntos de reunión seguros para todos los ocupantes."},{"label":"Roles y Responsabilidades:","text":"Definición de roles y responsabilidades para el personal clave durante una emergencia. Designación de un equipo de respuesta a emergencias, que incluye líderes y coordinadores."},{"label":"Procedimientos de Primeros Auxilios:","text":"Instrucciones para proporcionar primeros auxilios básicos y manejo de lesiones. Ubicación de kits de primeros auxilios y equipos de emergencia."},{"label":"Plan de Recuperación:","text":"Estrategias para la recuperación y restauración de operaciones normales después de la emergencia. Procedimientos para evaluar los daños y gestionar la reconstrucción."},{"label":"Capacitación y Simulacros:","text":"Programación de capacitaciones regulares para el personal en procedimientos de emergencia. Realización de simulacros para practicar y evaluar la efectividad del plan."}]}]}]'
    FROM dbo.ServiceCategory WHERE Name = N'Gestión empresarial';
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Service WHERE Slug = N'consultoria-gerencial')
    INSERT INTO dbo.Service (Slug, CategoryId, Title, Description, HeroImageUrl, Active, Locked, TabsJson)
    SELECT N'consultoria-gerencial', Id, N'Consultoría Gerencial', N'Proveemos a su empresa con un servicio integral de consultoría gerencial para fortalecer la toma de decisiones estratégicas. A través de nuestro enfoque en la Inteligencia y Planificación Estratégica (IPS), le ayudamos a anticipar cambios en el entorno empresarial, gestionar riesgos y aprovechar oportunidades. Trabajamos de la mano con la alta dirección para diseñar y ejecutar planes que maximicen el rendimiento de su empresa.', N'/assets/FondoFormacionALaMedida.jpg', 1, 1, N'[{"label":"SST","sections":[{"heading":"Seguridad y Salud en el Trabajo (SG-SST)","text":"Si necesita diseñar, implementar o actualizar su sistema de seguridad y salud en el trabajo, Lidessa está aquí para asistirle. Comenzaremos el proceso con usted o con la persona que designe al interior de su entidad, siguiendo los lineamientos del Decreto 1072 de 2015, la resolución 0312 de 2019 y todas aquellas normas que las sustituyan o modifiquen."},{"heading":"Actividades Presenciales:","bullets":[{"text":"Levantamiento de información para planes de emergencia, inspección de equipos de seguridad y elaboración de inventarios."},{"text":"Capacitaciones en primeros auxilios y evacuaciones."},{"text":"Gestión del riesgo para residentes y empleados."},{"text":"Evaluación de señaléticas de emergencia, demarcaciones y guías."},{"text":"Análisis integral de puestos de trabajo."},{"text":"Seguimiento de proveedores y contratistas."},{"text":"Acompañamiento en visitas y revisiones de entidades de regulación."},{"text":"Simulación de eventos e intervención de accidentados."},{"text":"Jornada de salud con actividades de promoción, prevención y diagnóstico."},{"text":"Inspecciones locativas y levantamiento de perfiles sociodemográficos laborales y habitacionales."},{"text":"Reuniones informativas y de gestión, tanto virtuales como presenciales."}]},{"heading":"Actividades a Distancia:","bullets":[{"text":"Diseño o rediseño del SG-SST según cambios normativos y necesidades del cliente."},{"text":"Elaboración de planes de emergencia y administración del SG-SST."},{"text":"Elaboración de manuales de funciones del SST y asesoría virtual en SST."},{"text":"Capacitación virtual para residentes, empleados y administradores."},{"text":"Gestión documental."},{"text":"Elaboración de sistemas de vigilancia epidemiológica (SVE)."},{"text":"Elaboración de informes detallados de inspecciones y análisis de perfiles sociodemográficos."},{"text":"Certificación y registro del sistema ante el fondo de riesgos laborales y la ARL."}]},{"text":"En la fase de diseño, garantizamos que su empresa cumpla con todos los requisitos de SST, incluyendo la formación de comités, capacitación y la inducción de los empleados. Revisaremos la matriz de riesgos y las normativas correspondientes, y al final de esta fase, validaremos y certificaremos que el diseño del SG-SST cumple con los requisitos mínimos establecidos por la normativa vigente. Implementaremos los procesos necesarios para asegurar que su SG-SST funcione de manera efectiva durante todo el año, manteniendo su continuidad y efectividad."},{"boxed":true,"text":"Licencia SG-SST No S2017060095482 de 2017 y S2018060230500 de 2018, por medio de las cuales la secretaría de Salud del Departamento de Antioquia concede licencia para ofertar servicios de seguridad y salud en el trabajo a Nivel nacional a persona jurídica."}]},{"label":"Administración Asistida","sections":[{"heading":"Administración asistida","text":"En nuestra empresa, ofrecemos un servicio integral de administración asistida diseñado para optimizar el funcionamiento y la eficiencia de su organización. Este servicio se centra en apoyar y mejorar las operaciones administrativas de su empresa mediante una gestión experta y personalizada. Características Clave del Servicio:","bullets":[{"label":"Gestión Administrativa Personalizada:","text":"Nos encargamos de las tareas administrativas diarias, adaptándonos a las necesidades específicas de su empresa para asegurar un funcionamiento fluido y eficiente."},{"label":"Optimización de Procesos:","text":"Evaluamos y mejoramos los procesos administrativos existentes, implementando prácticas y herramientas que aumentan la eficiencia y reducen costos operativos."},{"label":"Soporte en la Toma de Decisiones:","text":"Proporcionamos análisis y recomendaciones basadas en datos para apoyar la toma de decisiones estratégicas y operativas, ayudándole a alcanzar sus objetivos empresariales."},{"label":"Coordinación de Recursos:","text":"Gestionamos los recursos humanos y materiales necesarios para las operaciones administrativas, asegurando que su equipo y sus recursos estén alineados y optimizados."},{"label":"Cumplimiento Normativo:","text":"Aseguramos que todas las prácticas y procesos administrativos cumplan con las normativas y regulaciones vigentes, reduciendo riesgos y evitando sanciones."},{"label":"Gestión de Documentación y Registros:","text":"Manejamos la documentación y los registros importantes de su empresa de manera organizada y segura, garantizando un acceso eficiente y el cumplimiento de los requisitos legales."},{"label":"Capacitación y Desarrollo:","text":"Ofrecemos formación y apoyo continuo a su equipo en áreas clave de administración para mejorar sus habilidades y capacidades, fomentando un entorno de trabajo más productivo."},{"label":"Informe y Retroalimentación:","text":"Proporcionamos informes periódicos sobre el desempeño administrativo, junto con retroalimentación y recomendaciones para la mejora continua."}]},{"heading":"Beneficios del Servicio:","bullets":[{"label":"Eficiencia Operativa:","text":"Procesos administrativos más ágiles y organizados en el día a día."},{"label":"Reducción de Costos:","text":"Optimización de recursos que disminuye gastos operativos innecesarios."},{"label":"Mejora en la Toma de Decisiones:","text":"Información y análisis oportunos que respaldan decisiones estratégicas."},{"label":"Cumplimiento Asegurado:","text":"Prácticas administrativas alineadas con la normativa vigente."}]},{"text":"Con nuestro servicio de administración asistida, su empresa puede beneficiarse de una gestión administrativa experta y eficiente, permitiéndole centrarse en lo que realmente importa: hacer crecer su negocio."}]},{"label":"Consultoría en Gestión RRHH","sections":[{"heading":"Consultoría y gestión RRHH","text":"En nuestra empresa, ofrecemos un servicio integral de Recursos Humanos (RRHH) diseñado para optimizar la gestión del personal y fomentar un entorno laboral eficiente y positivo. Nuestro objetivo es apoyar a las empresas en todas las áreas relacionadas con el manejo de su capital humano, asegurando el alineamiento con los objetivos organizacionales y el cumplimiento de las normativas vigentes.","bullets":[{"label":"Reclutamiento y Selección:","text":"Gestionamos todo el proceso de reclutamiento, desde la publicación de vacantes hasta la selección de candidatos adecuados. Utilizamos técnicas avanzadas para asegurar que atraiga y elija a los mejores talentos para su empresa."},{"label":"Capacitación y Desarrollo:","text":"Diseñamos e implementamos programas de formación y desarrollo para mejorar las habilidades y competencias de sus empleados. Esto incluye formación inicial, capacitación continua y programas de liderazgo."},{"label":"Gestión del Desempeño:","text":"Implementamos sistemas para la evaluación y gestión del desempeño, proporcionando herramientas y procesos para medir y mejorar la productividad y el compromiso de los empleados."},{"label":"Compensación y Beneficios:","text":"Desarrollamos y gestionamos estructuras de compensación competitivas, así como paquetes de beneficios que atraigan y retengan talento, asegurando que cumplan con las expectativas y normativas laborales."},{"label":"Bienestar y Clima Laboral:","text":"Promovemos el bienestar de los empleados mediante programas de salud, seguridad y bienestar, y trabajamos para mantener un clima laboral positivo y productivo."},{"label":"Cumplimiento Normativo:","text":"Aseguramos que todas las prácticas y políticas de Recursos Humanos cumplan con la legislación laboral vigente y las normativas locales e internacionales, reduciendo riesgos y garantizando el cumplimiento."},{"label":"Gestión de Relaciones Laborales:","text":"Manejamos las relaciones con los empleados y sindicatos, resolviendo conflictos y promoviendo una comunicación abierta y efectiva."},{"label":"Administración de Nómina:","text":"Gestionamos la nómina de manera precisa y oportuna, asegurando que los salarios, impuestos y beneficios se administren correctamente."},{"label":"Consultoría Estratégica:","text":"Ofrecemos asesoramiento estratégico en la planificación de recursos humanos, alineando las prácticas de RRHH con los objetivos y estrategias generales de la empresa."}]}]}]'
    FROM dbo.ServiceCategory WHERE Name = N'Gestión empresarial';
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Service WHERE Slug = N'manuales-convivencia')
    INSERT INTO dbo.Service (Slug, CategoryId, Title, Description, HeroImageUrl, Active, Locked, TabsJson)
    SELECT N'manuales-convivencia', Id, N'Manuales de Convivencia Escolar', N'Creamos y actualizamos manuales de convivencia claros, inclusivos y conformes a la normativa educativa vigente en Colombia.', N'/assets/Manuales de Convivencia.jpg', 1, 1, N'[{"label":"Manuales de Convivencia","sections":[{"text":"El Manual de Convivencia es un documento esencial que establece las normas, derechos y deberes de todos los miembros de la comunidad educativa. Lidessa se especializa en la creación y actualización de manuales de convivencia que cumplen con las normativas educativas vigentes en Colombia, garantizando un ambiente escolar armonioso y respetuoso. Nuestro enfoque es asegurar que el manual sea claro, inclusivo y adaptado a las necesidades específicas de cada institución, promoviendo una cultura de paz y convivencia dentro del entorno escolar."},{"heading":"Puntos a destacar","bullets":[{"label":"Cumplimiento normativo:","text":"alineado con la Ley 1620 de 2013 y sus decretos reglamentarios."},{"label":"Derechos y deberes claros:","text":"define las responsabilidades de estudiantes, docentes, padres y directivos."},{"label":"Rutas de atención:","text":"establece protocolos y comités para la gestión de situaciones de convivencia."},{"label":"Construcción participativa:","text":"se elabora con la comunidad educativa para garantizar su legitimidad y apropiación."}]},{"heading":"Nuestro acompañamiento incluye","text":"Trabajamos de la mano con directivos, docentes, estudiantes y padres de familia durante todo el proceso, desde el diagnóstico inicial hasta la socialización y aprobación final del documento por el consejo directivo.","bullets":[{"label":"Diagnóstico institucional:","text":"revisión del manual vigente y de las situaciones de convivencia más frecuentes."},{"label":"Redacción y actualización:","text":"ajuste de capítulos obligatorios y complementarios con lenguaje claro e inclusivo."},{"label":"Validación con la comunidad:","text":"talleres de socialización con estudiantes, docentes y padres de familia."},{"label":"Aprobación institucional:","text":"acompañamiento hasta la aprobación por el consejo directivo y su divulgación."}]}]}]'
    FROM dbo.ServiceCategory WHERE Name = N'Instituciones educativas';
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Service WHERE Slug = N'proyecto-educativo')
    INSERT INTO dbo.Service (Slug, CategoryId, Title, Description, HeroImageUrl, Active, Locked, TabsJson)
    SELECT N'proyecto-educativo', Id, N'Diseño de PEI', N'Diseñamos e implementamos el Proyecto Educativo Institucional, alineado con la misión, visión y directrices del Ministerio de Educación Nacional.', N'/assets/Diseño de PEI.jpg', 1, 1, N'[{"label":"Diseño de PEI","title":"Diseño de PEI","intro":"El Proyecto Educativo Institucional (PEI) es el documento que define la identidad de la institución educativa, su misión, visión, objetivos y estrategias pedagógicas. En Lidessa, nos encargamos del diseño y la implementación del PEI, asegurando que esté alineado con los principios y objetivos de la institución, así como con las directrices del Ministerio de Educación Nacional. Nuestro servicio incluye la formulación de planes estratégicos que guían el desarrollo integral de la comunidad educativa, fomentando una educación de calidad.","checklist":["Definimos con claridad la misión, visión y principios que orientan a la institución.","Alineamos el documento con el Decreto 1860 de 1994 y las directrices vigentes del Ministerio de Educación Nacional.","Articulamos el modelo pedagógico y el enfoque curricular con las prácticas cotidianas del aula.","Formulamos planes de mejoramiento con metas medibles para el desarrollo integral de la comunidad educativa.","Acompañamos el proceso de construcción colectiva con directivos, docentes, padres y estudiantes hasta su aprobación."],"structure":{"title":"Nuestro proceso de acompañamiento","steps":["Diagnóstico del PEI vigente y su coherencia institucional","Construcción colectiva con la comunidad educativa","Redacción técnica y pedagógica del documento","Validación y aprobación por el consejo directivo","Socialización y seguimiento al plan de mejoramiento"]}}]'
    FROM dbo.ServiceCategory WHERE Name = N'Instituciones educativas';
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Service WHERE Slug = N'mallas-curriculares')
    INSERT INTO dbo.Service (Slug, CategoryId, Title, Description, HeroImageUrl, Active, Locked, TabsJson)
    SELECT N'mallas-curriculares', Id, N'Diseño de Mallas Curriculares', N'Elaboramos y revisamos mallas curriculares que organizan los contenidos educativos de forma secuencial y coherente, garantizando el desarrollo progresivo de competencias en los estudiantes.', N'/assets/Mallas Curriculares.jpg', 1, 1, N'[{"label":"Mallas Curriculares","sections":[{"text":"Las Mallas Curriculares son la estructura que organiza los contenidos educativos de manera secuencial y coherente, garantizando el desarrollo progresivo de competencias en los estudiantes. Lidessa ofrece la elaboración y revisión de mallas curriculares adaptadas a las necesidades específicas de cada institución, asegurando que estén en consonancia con los estándares de calidad educativa establecidos por el gobierno. Nuestro objetivo es facilitar el aprendizaje significativo y preparar a los estudiantes para los retos del siglo XXI."},{"heading":"Puntos a destacar","bullets":[{"label":"Análisis de procesos:","text":"revisión de los procesos curriculares actuales de la institución."},{"label":"Viabilidad de recursos:","text":"evaluación de los recursos disponibles para la implementación."},{"label":"Viabilidad del sector:","text":"análisis del contexto y las necesidades propias del sector educativo."},{"label":"Diseño de estructura curricular:","text":"organización secuencial y coherente de los contenidos por asignatura y grado."},{"label":"Acompañamiento en registro y radicación:","text":"apoyo en los trámites formales ante las autoridades educativas."}]},{"heading":"Nuestro acompañamiento incluye","text":"Trabajamos de la mano con directivos y docentes durante todo el proceso, desde el diagnóstico de las mallas existentes hasta su articulación con los referentes normativos vigentes.","bullets":[{"label":"Diagnóstico curricular:","text":"revisión de las mallas actuales por asignatura y grado."},{"label":"Articulación normativa:","text":"alineación con los Estándares Básicos de Competencias y los DBA del MEN."},{"label":"Secuenciación por ciclos:","text":"progresión coherente de competencias y desempeños entre grados."},{"label":"Socialización institucional:","text":"validación de la malla con docentes y directivos por área."}]}]}]'
    FROM dbo.ServiceCategory WHERE Name = N'Instituciones educativas';
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Service WHERE Slug = N'evaluacion-docente')
    INSERT INTO dbo.Service (Slug, CategoryId, Title, Description, HeroImageUrl, Active, Locked, TabsJson)
    SELECT N'evaluacion-docente', Id, N'Evaluación Docente', N'Ofrecemos un servicio integral de evaluación de docentes, basado en criterios objetivos y alineado con las normativas del Ministerio de Educación Nacional.', N'/assets/Evaluación Docente.jpg', 1, 1, N'[{"label":"Evaluación Docente","sections":[{"text":"La Evaluación de Docentes es un proceso fundamental para asegurar la calidad educativa dentro de una institución. Lidessa proporciona un servicio integral de evaluación de docentes, basado en criterios objetivos y alineado con las normativas del Ministerio de Educación Nacional. Nuestro enfoque es identificar fortalezas y áreas de mejora, ofreciendo recomendaciones para el desarrollo profesional continuo de los educadores y garantizando así una enseñanza efectiva y de alta calidad."},{"heading":"Puntos a destacar","bullets":[{"label":"Criterios objetivos:","text":"instrumentos de evaluación alineados con el modelo pedagógico institucional."},{"label":"Cumplimiento normativo:","text":"conforme a las directrices del Ministerio de Educación Nacional."},{"label":"Identificación de fortalezas:","text":"reconoce el buen desempeño y las áreas de mejora de cada docente."},{"label":"Desarrollo profesional:","text":"recomendaciones concretas para la formación continua de los educadores."}]},{"heading":"Nuestro acompañamiento incluye","text":"Acompañamos el proceso completo de evaluación: diseño, socialización, aplicación, análisis y retroalimentación, hasta la construcción de planes de desarrollo profesional docente.","bullets":[{"label":"Diseño del sistema:","text":"instrumentos de observación de aula, autoevaluación y coevaluación docente."},{"label":"Aplicación y análisis:","text":"socialización con docentes y directivos, y análisis de resultados por categorías."},{"label":"Retroalimentación:","text":"devolución constructiva e individual a cada docente evaluado."},{"label":"Planes de desarrollo:","text":"programas de formación diferenciada y seguimiento al desarrollo profesional."}]}]}]'
    FROM dbo.ServiceCategory WHERE Name = N'Instituciones educativas';
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Service WHERE Slug = N'evaluacion-institucional')
    INSERT INTO dbo.Service (Slug, CategoryId, Title, Description, HeroImageUrl, Active, Locked, TabsJson)
    SELECT N'evaluacion-institucional', Id, N'Evaluación Institucional', N'Ofrecemos un servicio de evaluación institucional exhaustivo que acompaña a las instituciones educativas en su proceso de mejora continua y cumplimiento normativo.', N'/assets/Evaluación Institucional.jpg', 1, 1, N'[{"label":"Evaluación Institucional","sections":[{"text":"La Evaluación Institucional es un proceso de diagnóstico y análisis que permite a las instituciones educativas medir su desempeño y efectividad en la consecución de sus objetivos. Lidessa ofrece un servicio de evaluación institucional exhaustivo, que incluye la recolección y análisis de datos, la identificación de áreas críticas y la propuesta de planes de mejora. Nuestro objetivo es acompañar a las instituciones en su proceso de mejora continua, asegurando la excelencia en la educación y el cumplimiento de las normativas educativas nacionales."},{"heading":"Puntos a destacar","bullets":[{"label":"Diagnóstico integral:","text":"evalúa las cuatro áreas de gestión: directiva, académica, administrativa y de comunidad."},{"label":"Análisis de datos:","text":"recolección y análisis riguroso de la información institucional."},{"label":"Identificación de áreas críticas:","text":"prioriza las oportunidades de mejora más relevantes."},{"label":"Cumplimiento normativo:","text":"conforme a las directrices educativas nacionales vigentes."}]},{"heading":"Nuestro acompañamiento incluye","text":"Acompañamos la autoevaluación institucional anual bajo la guía del MEN y la convertimos en un Plan de Mejoramiento Institucional con metas claras y seguimiento continuo.","bullets":[{"label":"Autoevaluación anual:","text":"evaluación de las cuatro áreas de gestión bajo los lineamientos del MEN."},{"label":"Análisis de resultados:","text":"consolidación por área, tendencias históricas e informe ejecutivo para directivos."},{"label":"Plan de Mejoramiento (PMI):","text":"definición de metas, indicadores, responsables y cronograma."},{"label":"Seguimiento y control:","text":"mecanismos para verificar el avance de los compromisos definidos."}]}]}]'
    FROM dbo.ServiceCategory WHERE Name = N'Instituciones educativas';
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Service WHERE Slug = N'formacion-continua')
    INSERT INTO dbo.Service (Slug, CategoryId, Title, Description, HeroImageUrl, Active, Locked, TabsJson)
    SELECT N'formacion-continua', Id, N'Formación Continua', N'Ofrecemos programas de formación continua para profesionales y equipos de trabajo, orientados a actualizar competencias, fortalecer habilidades y mantenerse al día con las normativas del sector.', N'/assets/Formación_Continua.jpg', 1, 1, N'[{"label":"Nuestro enfoque","sections":[{"text":"En Lidessa creemos que la capacitación continua es clave para el éxito y la adaptación de las empresas en un entorno en constante cambio. La Formación Continua está orientada a proporcionar a los empleados y directivos de las empresas conocimientos actualizados y habilidades prácticas en temas relacionados con el Sistema de Gestión de Seguridad y Salud en el Trabajo (SG-SST) y otras áreas relevantes. Nuestros cursos, que se pueden realizar de manera presencial o virtual, están diseñados para mantener a los participantes al día con las últimas normativas y mejores prácticas, contribuyendo a la mejora continua de la organización."},{"heading":"Puntos a destacar","bullets":[{"text":"Capacitación actualizada"},{"text":"Modalidades flexibles"},{"text":"Expertos en la materia"},{"text":"Certificación"}]}]}]'
    FROM dbo.ServiceCategory WHERE Name = N'Educación y formación';
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Service WHERE Slug = N'formacion-medida')
    INSERT INTO dbo.Service (Slug, CategoryId, Title, Description, HeroImageUrl, Active, Locked, TabsJson)
    SELECT N'formacion-medida', Id, N'Formación a la Medida', N'Diseñamos programas de capacitación corporativa completamente personalizados, adaptados a los objetivos estratégicos, la cultura organizacional y las necesidades específicas de su equipo.', N'/assets/Formación_Medida.jpg', 1, 1, N'[{"label":"Beneficios","sections":[{"text":"La Formación a la Medida de Lidessa está diseñada para ofrecer soluciones de capacitación personalizadas que se ajusten a las necesidades específicas de cada empresa. Este servicio permite desarrollar programas de formación adaptados a los desafíos particulares y objetivos estratégicos de la organización, garantizando que la capacitación sea relevante y directamente aplicable a su contexto."},{"heading":"Puntos a destacar","bullets":[{"label":"Diseño personalizado:","text":"programas de formación desarrollados con base en un análisis de las necesidades específicas de la empresa."},{"label":"Flexibilidad:","text":"capacitación en horarios y formatos que se adaptan a la dinámica laboral de la organización."},{"label":"Relevancia:","text":"contenidos y metodologías ajustados a los retos y objetivos particulares de la empresa."},{"label":"Soporte continuo:","text":"asesoramiento y seguimiento durante y después de la formación para asegurar la implementación efectiva de los conocimientos adquiridos."}]}]}]'
    FROM dbo.ServiceCategory WHERE Name = N'Educación y formación';
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Service WHERE Slug = N'programas-instituciones')
    INSERT INTO dbo.Service (Slug, CategoryId, Title, Description, HeroImageUrl, Active, Locked, TabsJson)
    SELECT N'programas-instituciones', Id, N'Programas para Instituciones Educativas', N'Diseñamos e implementamos programas formativos integrales dirigidos a directivos docentes, docentes y personal administrativo de instituciones educativas públicas y privadas.', N'/assets/educativa.jpg', 1, 1, N'[{"label":"Instituciones educativas","sections":[{"text":"En Lidessa ofrecemos servicios para el correcto funcionamiento de instituciones educativas, instituciones de educación superior, instituciones de formación y desarrollo humano e instituciones de educación formal integral. Incluimos programas de contenidos académicos de pregrado y postgrado, y asesoramos a su institución con diseño y contenido para plataformas virtuales, tales como videos, infografías, flyers y evaluaciones, entre otros."},{"heading":"Puntos a destacar","bullets":[{"text":"Instituciones de educación superior"},{"text":"Instituciones de formación y desarrollo humano"},{"text":"Instituciones de educación formal integral"},{"text":"Contenidos académicos de pregrado y postgrado"},{"text":"Diseño de videos, infografías, flyers y evaluaciones para plataformas virtuales"}]}]}]'
    FROM dbo.ServiceCategory WHERE Name = N'Educación y formación';
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Service WHERE Slug = N'seguridad-salud')
    INSERT INTO dbo.Service (Slug, CategoryId, Title, Description, HeroImageUrl, Active, Locked, TabsJson)
    SELECT N'seguridad-salud', Id, N'Seguridad y Salud en el Trabajo (SG-SST)', N'Proveemos a su empresa con un servicio integral de consultoría gerencial para fortalecer la toma de decisiones estratégicas. A través de nuestro enfoque en la Inteligencia y Planificación Estratégica (IPS), le ayudamos a anticipar cambios en el entorno empresarial, gestionar riesgos y aprovechar oportunidades. Trabajamos de la mano con la alta dirección para diseñar y ejecutar planes que maximicen el rendimiento de su empresa.', N'/assets/seguridad_salud_trabajo.jpg', 1, 1, N'[{"label":"SG-SST","sections":[{"text":"Si necesita diseñar, implementar o actualizar su sistema de seguridad y salud en el trabajo, Lidessa está aquí para asistirle. Comenzaremos el proceso con usted o con la persona que designe al interior de su entidad, siguiendo los lineamientos del Decreto 1072 de 2015, la resolución 0312 de 2019 y todas aquellas normas que las sustituyan o modifiquen."},{"heading":"Actividades Presenciales","bullets":[{"text":"Levantamiento de información para planes de emergencia, inspección de equipos de seguridad y elaboración de inventarios."},{"text":"Capacitaciones en primeros auxilios y evacuaciones."},{"text":"Gestión del riesgo para residentes y empleados."},{"text":"Evaluación de señaléticas de emergencia, demarcaciones y guías."},{"text":"Análisis integral de puestos de trabajo."},{"text":"Seguimiento de proveedores y contratistas."},{"text":"Acompañamiento en visitas y revisiones de entidades de regulación."},{"text":"Simulación de eventos e intervención de accidentados."},{"text":"Jornada de salud con actividades de promoción, prevención y diagnóstico."},{"text":"Inspecciones locativas y levantamiento de perfiles sociodemográficos laborales y habitacionales."},{"text":"Reuniones informativas y de gestión, tanto virtuales como presenciales."}]},{"heading":"Actividades a Distancia","bullets":[{"text":"Diseño o rediseño del SG-SST según cambios normativos y necesidades del cliente."},{"text":"Elaboración de planes de emergencia y administración del SG-SST."},{"text":"Elaboración de manuales de funciones del SST y asesoría virtual en SST."},{"text":"Capacitación virtual para residentes, empleados y administradores."},{"text":"Gestión documental."},{"text":"Elaboración de sistemas de vigilancia epidemiológica (SVE)."},{"text":"Elaboración de informes detallados de inspecciones y análisis de perfiles sociodemográficos."},{"text":"Certificación y registro del sistema ante el fondo de riesgos laborales y la ARL."}]},{"text":"En la fase de diseño, garantizamos que su empresa cumpla con todos los requisitos de SST, incluyendo la formación de comités, capacitación y la inducción de los empleados. Revisaremos la matriz de riesgos y las normativas correspondientes, y al final de esta fase, validaremos y certificaremos que el diseño del SG-SST cumple con los requisitos mínimos establecidos por la normativa vigente. Implementaremos los procesos necesarios para asegurar que su SG-SST funcione de manera efectiva durante todo el año, manteniendo su continuidad y efectividad."},{"boxed":true,"text":"Licencia SG-SST No S2017060095482 de 2017 y S2018060230500 de 2018, por medio de las cuales la secretaría de Salud del Departamento de Antioquia concede licencia para ofertar servicios de seguridad y salud en el trabajo a Nivel nacional a persona jurídica."}]},{"label":"Administración Asistida","sections":[{"heading":"Administración asistida","text":"En nuestra empresa, ofrecemos un servicio integral de administración asistida diseñado para optimizar el funcionamiento y la eficiencia de su organización. Este servicio se centra en apoyar y mejorar las operaciones administrativas de su empresa mediante una gestión experta y personalizada. Características Clave del Servicio:","bullets":[{"label":"Gestión Administrativa Personalizada:","text":"Nos encargamos de las tareas administrativas diarias, adaptándonos a las necesidades específicas de su empresa para asegurar un funcionamiento fluido y eficiente."},{"label":"Optimización de Procesos:","text":"Evaluamos y mejoramos los procesos administrativos existentes, implementando prácticas y herramientas que aumentan la eficiencia y reducen costos operativos."},{"label":"Soporte en la Toma de Decisiones:","text":"Proporcionamos análisis y recomendaciones basadas en datos para apoyar la toma de decisiones estratégicas y operativas, ayudándole a alcanzar sus objetivos empresariales."},{"label":"Coordinación de Recursos:","text":"Gestionamos los recursos humanos y materiales necesarios para las operaciones administrativas, asegurando que su equipo y sus recursos estén alineados y optimizados."},{"label":"Cumplimiento Normativo:","text":"Aseguramos que todas las prácticas y procesos administrativos cumplan con las normativas y regulaciones vigentes, reduciendo riesgos y evitando sanciones."},{"label":"Gestión de Documentación y Registros:","text":"Manejamos la documentación y los registros importantes de su empresa de manera organizada y segura, garantizando un acceso eficiente y el cumplimiento de los requisitos legales."},{"label":"Capacitación y Desarrollo:","text":"Ofrecemos formación y apoyo continuo a su equipo en áreas clave de administración para mejorar sus habilidades y capacidades, fomentando un entorno de trabajo más productivo."},{"label":"Informe y Retroalimentación:","text":"Proporcionamos informes periódicos sobre el desempeño administrativo, junto con retroalimentación y recomendaciones para la mejora continua."}]},{"heading":"Beneficios del Servicio:","bullets":[{"label":"Eficiencia Operativa:","text":"Procesos administrativos más ágiles y organizados en el día a día."},{"label":"Reducción de Costos:","text":"Optimización de recursos que disminuye gastos operativos innecesarios."},{"label":"Mejora en la Toma de Decisiones:","text":"Información y análisis oportunos que respaldan decisiones estratégicas."},{"label":"Cumplimiento Asegurado:","text":"Prácticas administrativas alineadas con la normativa vigente."}]},{"text":"Con nuestro servicio de administración asistida, su empresa puede beneficiarse de una gestión administrativa experta y eficiente, permitiéndole centrarse en lo que realmente importa: hacer crecer su negocio."}]},{"label":"Consultoría en Gestión RRHH","sections":[{"heading":"Consultoría y gestión RRHH","text":"En nuestra empresa, ofrecemos un servicio integral de Recursos Humanos (RRHH) diseñado para optimizar la gestión del personal y fomentar un entorno laboral eficiente y positivo. Nuestro objetivo es apoyar a las empresas en todas las áreas relacionadas con el manejo de su capital humano, asegurando el alineamiento con los objetivos organizacionales y el cumplimiento de las normativas vigentes.","bullets":[{"label":"Reclutamiento y Selección:","text":"Gestionamos todo el proceso de reclutamiento, desde la publicación de vacantes hasta la selección de candidatos adecuados. Utilizamos técnicas avanzadas para asegurar que atraiga y elija a los mejores talentos para su empresa."},{"label":"Capacitación y Desarrollo:","text":"Diseñamos e implementamos programas de formación y desarrollo para mejorar las habilidades y competencias de sus empleados. Esto incluye formación inicial, capacitación continua y programas de liderazgo."},{"label":"Gestión del Desempeño:","text":"Implementamos sistemas para la evaluación y gestión del desempeño, proporcionando herramientas y procesos para medir y mejorar la productividad y el compromiso de los empleados."},{"label":"Compensación y Beneficios:","text":"Desarrollamos y gestionamos estructuras de compensación competitivas, así como paquetes de beneficios que atraigan y retengan talento, asegurando que cumplan con las expectativas y normativas laborales."},{"label":"Bienestar y Clima Laboral:","text":"Promovemos el bienestar de los empleados mediante programas de salud, seguridad y bienestar, y trabajamos para mantener un clima laboral positivo y productivo."},{"label":"Cumplimiento Normativo:","text":"Aseguramos que todas las prácticas y políticas de Recursos Humanos cumplan con la legislación laboral vigente y las normativas locales e internacionales, reduciendo riesgos y garantizando el cumplimiento."},{"label":"Gestión de Relaciones Laborales:","text":"Manejamos las relaciones con los empleados y sindicatos, resolviendo conflictos y promoviendo una comunicación abierta y efectiva."},{"label":"Administración de Nómina:","text":"Gestionamos la nómina de manera precisa y oportuna, asegurando que los salarios, impuestos y beneficios se administren correctamente."},{"label":"Consultoría Estratégica:","text":"Ofrecemos asesoramiento estratégico en la planificación de recursos humanos, alineando las prácticas de RRHH con los objetivos y estrategias generales de la empresa."}]}]}]'
    FROM dbo.ServiceCategory WHERE Name = N'Propiedades horizontales';
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Service WHERE Slug = N'manejo-residuos')
    INSERT INTO dbo.Service (Slug, CategoryId, Title, Description, HeroImageUrl, Active, Locked, TabsJson)
    SELECT N'manejo-residuos', Id, N'Plan de Manejo de Residuos', N'Diseñamos estrategias de crecimiento y transformación organizacional que fortalecen las capacidades internas de su empresa, optimizan recursos y potencian la competitividad en el mercado.', N'/assets/plan_de_manejo_residuales.jpg', 1, 1, N'[{"label":"RIT","title":"Reglamento Interno de Trabajo","intro":"Las empresas que tienen más de 5 trabajadores de carácter permanente si son comerciales, o más de diez (10) si son industriales, o más de veinte (20) si son agrícolas, ganaderas o forestales, necesitan un reglamento de trabajo, sin embargo, es recomendable que todas las empresas los tengan ya que esta es la carta de navegación para regular las relaciones laborales, además de otros aspectos importantes a considerar:","checklist":["Con la cultura actual y dinámica social y laboral, la tutela se ha convertido en un mecanismo de reclamación, los RIT bien implementados pueden prevenir tutelas por reintegro en casos de despido ilegal de sus trabajadores, así como el pago de todos los salarios y derechos dejados de percibir por ese trabajador desde que se despidió de forma ilegal, sumado a indemnización que contempla la Ley Laboral.","En el contexto de una relación laboral y los mismos niveles de informalidad que se manejan las mismas relaciones con los colaboradores, en el caso de dificultades o problema con un trabajador, el empleador se puede limitar a realizar una terminación de contrato, en muchos casos aunque exista una relación de peso que justifique un despido, al no contar con un reglamento Interno que imponga una sanción y un adecuado proceso de descargos, este despido aunque justificado, en la realidad se vuelve ilegal, lo que resulta en los riesgos de reintegro o indemnización.","Es importante tener en cuenta que el Ministerio de Trabajo puede imponer sanciones de hasta 5.000 Smlmv para las empresas que no cuenten con este documento. La sanción es graduable dependiendo de la gravedad de la falta, la reincidencia de la compañía y el tamaño de la misma."],"structure":{"title":"Estructura del Servicio","image":"/assets/Descripcion_Estructura.png","steps":["Diagnóstico de la compañía","Adecuación al marco legal","Reunión con gerencia, colaboradores y líderes de equipo","Proceso de aprobación","Publicación y optimización"]}},{"label":"Vinculación","sections":[{"heading":"Vinculación","text":"El proceso de vinculación inicia con un análisis detallado de los perfiles que la empresa requiere, seguido de una búsqueda exhaustiva en nuestro amplio banco de talentos. Luego, llevamos a cabo un proceso de selección riguroso, que incluye entrevistas, evaluaciones psicotécnicas y verificaciones de referencias, asegurando que los candidatos propuestos cumplan con los estándares exigidos."},{"heading":"De su interés","text":"En Colombia, la vinculación de empleados está regida por el Código Sustantivo del Trabajo y otras normativas laborales, como la Ley 789 de 2002 y la Ley 1562 de 2012. Estas leyes exigen que las empresas contraten de manera formal, cumpliendo con las obligaciones de seguridad social, riesgos laborales y garantizando condiciones dignas de trabajo. Las empresas que no cumplen con estas normativas pueden enfrentar sanciones como multas por parte del Ministerio del Trabajo, demandas laborales, y el riesgo de ser incluidas en listas negras que dificultan la contratación futura de personal. La falta de afiliación a la seguridad social o a riesgos laborales, por ejemplo, puede generar multas que varían entre 1 y 5.000 salarios mínimos mensuales legales vigentes, dependiendo de la gravedad del incumplimiento."},{"heading":"¿Cómo le ayuda Lidessa?","text":"Lidessa ofrece un servicio de vinculación integral que asegura que las empresas cumplan con todas las normativas laborales vigentes. Al gestionar el proceso de reclutamiento y selección de manera eficiente, Lidessa garantiza que los empleados contratados estén debidamente afiliados a los sistemas de seguridad social y riesgos laborales, reduciendo así el riesgo de sanciones. Además, la empresa ofrece asesoría continua para mantener la conformidad con las leyes laborales."}]},{"label":"Diseño de Estructura Interna","sections":[{"heading":"Diseño de Estructura Interna","text":"El buen funcionamiento de una organización depende en gran medida de su capacidad de clarificar y medir las acciones y funciones de sus colaboradores en relación a sus procesos. La estructura empresarial es la forma en que se organiza internamente una empresa. No hay una única estructura organizativa válida, sino diferentes tipos en función de los objetivos de la compañía. Se entiende por estructura empresarial la organización que se dota a una empresa para funcionar. El diseño organizacional implica conocer bien las cadenas de mando, puestos de trabajo, jerarquías y dependencias, así como los diversos departamentos en los que se ordena la compañía. Los responsables de diseñar la estructura organizativa cuentan en primer lugar con el organigrama de la empresa, pero no sólo eso."},{"heading":"La creación de la estructura organizacional de una empresa consta de dos pasos:","bullets":[{"text":"Analizar tareas, subtareas y definir metas de la organización."},{"text":"Llevar un control de la evaluación de objetivos."}]},{"heading":"Toda estructura organizacional cuenta con algunos componentes fundamentales que las distinguen de otras:","bullets":[{"label":"Cadena de Mando:","text":"Es la base de todo modelo organizacional. Se trata de una línea de autoridad que fluye desde la más alta dirección hasta los puestos más bajos. Esta cadena define a quién hay que dirigirse según el tema que se trate."},{"label":"Nivel de centralización:","text":"Que la organización esté centralizada o descentralizada contribuirá directamente en la velocidad del proceso de toma de decisiones. También tendrá un impacto en la percepción más o menos democrática de la forma de actuar."},{"label":"Margen de control:","text":"Es un elemento muy influido por el tamaño de la compañía y el nivel de centralización. Cuantos más empleados controle un mando directivo, mayor será su margen de control."},{"label":"Grado de especialización:","text":"Esta clave de la estructura organizacional aborda la división del trabajo. Dentro de una organización, las tareas se distribuyen en distintos niveles. Si los empleados cuentan con una especialización alta, estos serán expertos en su campo y serán más productivos."}]},{"heading":"Diseño estructural","text":"El diseño estructural se ocupa de la investigación metódica de la estabilidad, resistencia y rigidez de la estructura. El objetivo básico es crear una estructura que sea capaz de resistir todas las cargas aplicadas sin fallar durante su vida prevista. Algunos componentes básicos son:","bullets":[{"text":"Manuales de funciones."},{"text":"Llevar un control de la evaluación de objetivos."},{"text":"Herramientas de evaluación y desempeño."},{"text":"Estructuración funcional y de líneas de mando."},{"text":"Establecimiento de indicadores de gestión y operativos."},{"text":"Análisis del contexto interno y externo de la empresa."}]}]},{"label":"Manual de Funciones","sections":[{"heading":"Manual de Funciones","text":"El Manual de Funciones es una herramienta clave para definir claramente las responsabilidades, funciones y procesos dentro de una empresa. Lidessa ofrece la creación y actualización de manuales de funciones que alinean las operaciones con los objetivos estratégicos de la organización. Este servicio incluye la elaboración de un documento exhaustivo que detalla las funciones de cada puesto dentro de la organización, así como las competencias necesarias, los flujos de trabajo y los indicadores de desempeño. Este manual es fundamental para mejorar la eficiencia operativa, facilitar la formación del personal y asegurar la claridad en los roles y responsabilidades."},{"heading":"Lidessa le ofrece:","bullets":[{"label":"Diagnóstico organizacional:","text":"Análisis de la estructura actual de la empresa para identificar áreas de mejora."},{"label":"Definición de roles:","text":"Clarificación de funciones, responsabilidades y expectativas para cada puesto de trabajo."},{"label":"Optimización de procesos:","text":"Mejora en la asignación de tareas y en la coordinación interdepartamental."},{"label":"Herramienta de gestión:","text":"Un manual que se convierte en un referente para la gestión del talento y el desarrollo organizacional."}]},{"heading":"Recuerde:","boxed":true,"text":"El Manual de Funciones es un documento fundamental para el cumplimiento de varias normativas laborales en Colombia, incluyendo el Código Sustantivo del Trabajo y el Decreto 1072 de 2015. Este manual ayuda a formalizar y clarificar las responsabilidades de los empleados, facilitando la gestión de recursos humanos y asegurando que se cumplan los estándares de eficiencia y transparencia en las operaciones empresariales. La ausencia de un manual de funciones o la falta de claridad en los roles y responsabilidades puede llevar a conflictos laborales, sanciones por parte de entidades de inspección laboral, y demandas por incumplimientos contractuales. Las sanciones pueden incluir multas y, en casos extremos, la suspensión temporal de actividades hasta que se subsanen las deficiencias en la organización."}]},{"label":"PMIRS","sections":[{"heading":"Plan de Manejo Integral de Residuos Sólidos","text":"Los PMIRS o Planes de Manejo Integral de Residuos Sólidos, son planes estratégicos que buscan gestionar adecuadamente los residuos sólidos generados por la empresa, minimizando los riesgos para la salud de los trabajadores y el impacto ambiental. Estos programas incluyen la identificación, segregación, almacenamiento, transporte y disposición final de los residuos, asegurando que se cumplan con las normativas legales y se promueva un ambiente de trabajo seguro y saludable, reduciendo posibles accidentes, enfermedades ocupacionales, y contribuyendo a la sostenibilidad. Para el acompañamiento del Plan de Manejo Integral de Residuos Sólidos se desarrollan las siguientes actividades:","bullets":[{"text":"Identificar por medio de un diagnóstico inicial, cuál es el estado actual de los procedimientos encargados de la gestión integral de los residuos sólidos en la entidad, evaluando cada una de sus etapas. (Generación, movimiento interno de residuos, almacenamiento, transporte y disposición final)."},{"text":"Desarrollar estrategias de formación y educación para incentivar a la adecuada separación en la fuente de los residuos generados en la entidad, con el propósito de optimizar los residuos aprovechables, y así disminuir el volumen de residuos depositados en el centro de acopio. (campañas de sensibilización y educación ambiental)."},{"text":"Adoptar un plan de acción que incluya prevención, separación, almacenamiento, segregación, recolección, tratamiento y disposición final."}]}]},{"label":"RP","title":"Riesgo Psicosocial","sections":[{"boxed":true,"text":"Licencia SG-SST No S2017060095482 de 2017 y S2018060230500 de 2018, por medio de las cuales la secretaría de Salud del Departamento de Antioquia concede licencia para ofertar servicios de seguridad y salud en el trabajo a Nivel nacional a persona jurídica."},{"text":"El Ministerio de la Protección Social (ahora Ministerio de Trabajo) y la Universidad Javeriana desarrollaron los únicos formatos autorizados que cumplen con los criterios técnicos establecidos por la Resolución 2764 del 2022, para el uso en territorio colombiano. Hasta la fecha los cuestionarios de la batería de medición de riesgo psicosocial deben aplicarse utilizando lapicero y papel, de manera presencial. Bajo ninguna circunstancia puede realizarse la medición del riesgo psicosocial utilizando medios virtuales como páginas web, cuestionarios magnéticos ni otros métodos."},{"text":"Nuestra empresa se especializa en la identificación, evaluación y prevención de riesgos psicosociales en el trabajo. Entendemos que los factores psicosociales pueden afectar negativamente la salud mental y física de los trabajadores, así como la productividad y el bienestar de las organizaciones. Nuestros servicios abarcan la evaluación de los siguientes factores de riesgo psicosocial:","bullets":[{"label":"Factores intralaborales:","text":"demanda de trabajo, control y apoyo social en el trabajo, claridad de rol y expectativas, entre otros."},{"label":"Factores extralaborales:","text":"apoyo social fuera del trabajo, equilibrio entre la vida laboral y personal, entre otros."},{"label":"Estrés:","text":"identificamos las fuentes de estrés laboral y sus efectos en la salud y el bienestar."}]},{"heading":"Nuestros servicios incluyen:","bullets":[{"text":"Evaluación de los factores de riesgo psicosocial mediante baterías de instrumentos validados."},{"text":"Análisis de los resultados y recomendaciones para la implementación de medidas de prevención y control."},{"text":"Diseño y ejecución de programas de intervención para la promoción de la salud mental y el bienestar en el trabajo."}]},{"heading":"Importante","boxed":true,"text":"La multa que se le asignará, debido a que el Decreto 472 del 17 de marzo de 2015 estableció sanciones que inician desde los 50 salarios mínimos legales vigentes. Adicionalmente recuerde que la Resolución 1111 de 2017 establece los Estándares Mínimo de obligatorio cumplimiento en Seguridad y Salud en el Trabajo, con los cuales la empresa vela por el bienestar físico y mental del empleado. Recuerde que la única persona autorizada en Colombia para realizar la evaluación y generar diagnósticos para la prevención y el control de enfermedades generadas por los riesgos psicosociales, es un Psicólogo graduado como Especialista en Seguridad y Salud en el Trabajo."}]},{"label":"TD","title":"Tratamiento de Bases de datos","sections":[{"text":"La gestión de bases de datos en Colombia está regulada principalmente por la Ley 1581 de 2012 (Ley de Protección de Datos Personales) y su Decreto 1081 de 2015. Estas normativas imponen a las empresas la responsabilidad de garantizar la seguridad, confidencialidad y correcto uso de los datos personales que manejan. Para cumplir con estas obligaciones, es fundamental implementar medidas técnicas y organizativas que protejan adecuadamente la información sensible, evitando su uso indebido y asegurando la privacidad de los titulares de los datos."},{"text":"En Lidessa ofrecemos un servicio especializado en el diseño e implementación del marco legal para el tratamiento de datos personales, asegurando el cumplimiento de todas las normativas vigentes. Nuestro enfoque integral abarca desde la creación de políticas de tratamiento de datos hasta la gestión del riesgo asociado a los procesos de manejo de datos personales. Entendemos que cada empresa tiene necesidades específicas, por lo que diseñamos manuales personalizados que incluyen guías, instructivos y herramientas para el adecuado tratamiento de datos personales, conforme a la ley."},{"text":"Además, proporcionamos una caracterización detallada de los datos organizacionales, identificando los tipos de datos tratados y estableciendo mecanismos claros para su manejo seguro. También ofrecemos soporte en la definición de un Oficial de Protección de Datos Personales (OPD) y la conformación de un comité interno, integrado por diferentes dependencias de la organización, que toma decisiones estratégicas en relación con el tratamiento de datos. Asimismo, nos encargamos de realizar el registro ante la Superintendencia de Industria y Comercio (SIC) cuando es requerido."},{"text":"Con Lidessa, las empresas no solo aseguran el cumplimiento normativo, sino que también protegen su reputación corporativa, evitando sanciones costosas y garantizando que los derechos de los titulares de los datos sean respetados."},{"heading":"Le ofrecemos:","bullets":[{"label":"Diseño personalizado:","text":"Creación de bases de datos estructuradas según las necesidades y procesos específicos de la empresa, incluyendo manuales de tratamiento de datos y políticas alineadas con la normativa vigente."},{"label":"Mantenimiento continuo:","text":"Actualización y optimización de las bases de datos para asegurar su eficiencia y cumplimiento a largo plazo, adaptándose a los cambios regulatorios y tecnológicos."},{"label":"Integración de sistemas:","text":"Conexión de la base de datos con otros sistemas de la empresa para un flujo de información eficiente y seguro, asegurando que todos los procesos estén alineados y sean consistentes en el manejo de datos."},{"label":"Capacitación normativa:","text":"Formación especializada para el personal, enfocada en las leyes de protección de datos personales, para garantizar que todos en la organización comprendan y apliquen correctamente las normativas."},{"label":"Gestión del riesgo:","text":"Evaluación y mitigación de riesgos asociados al tratamiento de datos personales, asegurando que la empresa esté preparada para manejar cualquier eventualidad de manera efectiva."},{"label":"Registro ante la SIC:","text":"Asesoría y gestión del registro de bases de datos ante la Superintendencia de Industria y Comercio, cumpliendo con los requisitos legales."}]}]},{"label":"Planes de emergencia","title":"Planes de emergencia","sections":[{"text":"Un Plan de Emergencia es un conjunto de procedimientos y directrices diseñados para manejar situaciones imprevistas o crisis de manera efectiva. Este plan tiene como objetivo minimizar el impacto de emergencias, garantizar la seguridad de las personas y proteger los activos de la organización. Un plan de emergencia bien elaborado permite una respuesta organizada y eficiente ante eventos como incendios, desastres naturales, accidentes graves, fallos de infraestructura o cualquier otra situación de emergencia que pueda afectar a una organización o comunidad."},{"heading":"Le ofrecemos:","bullets":[{"label":"Identificación de Riesgos:","text":"Evaluación y análisis de los riesgos potenciales que podrían afectar a la organización o comunidad. Clasificación de riesgos según su probabilidad y severidad."},{"label":"Procedimientos de Evacuación:","text":"Diseño de rutas de evacuación claras y señalizadas. Establecimiento de puntos de reunión seguros para todos los ocupantes."},{"label":"Roles y Responsabilidades:","text":"Definición de roles y responsabilidades para el personal clave durante una emergencia. Designación de un equipo de respuesta a emergencias, que incluye líderes y coordinadores."},{"label":"Procedimientos de Primeros Auxilios:","text":"Instrucciones para proporcionar primeros auxilios básicos y manejo de lesiones. Ubicación de kits de primeros auxilios y equipos de emergencia."},{"label":"Plan de Recuperación:","text":"Estrategias para la recuperación y restauración de operaciones normales después de la emergencia. Procedimientos para evaluar los daños y gestionar la reconstrucción."},{"label":"Capacitación y Simulacros:","text":"Programación de capacitaciones regulares para el personal en procedimientos de emergencia. Realización de simulacros para practicar y evaluar la efectividad del plan."}]}]}]'
    FROM dbo.ServiceCategory WHERE Name = N'Propiedades horizontales';
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Service WHERE Slug = N'datos-personales')
    INSERT INTO dbo.Service (Slug, CategoryId, Title, Description, HeroImageUrl, Active, Locked, TabsJson)
    SELECT N'datos-personales', Id, N'Tratamiento de Datos Personales', N'Implementamos políticas y procedimientos de protección de datos personales conforme a la Ley 1581 de 2012 y sus decretos reglamentarios, para propiedades horizontales, conjuntos residenciales y empresas.', N'https://images.unsplash.com/photo-1558494949-ef010cbdcc31?w=1400&h=600&fit=crop&auto=format', 1, 1, N'[{"label":"Marco legal","sections":[{"text":"La Ley Estatutaria 1581 de 2012 y el Decreto 1377 de 2013 regulan el tratamiento de datos personales en Colombia. Todas las organizaciones deben cumplirlas."},{"heading":"Puntos a destacar","bullets":[{"text":"Ley 1581/2012 – Habeas Data"},{"text":"Decreto 1377/2013 – Reglamentación"},{"text":"Circulares de la SIC vigentes"},{"text":"Sanciones por incumplimiento: hasta 2.000 SMMLV"}]}]},{"label":"Implementación","sections":[{"text":"Desarrollamos el sistema de gestión de datos personales completo: políticas, avisos, registros y procedimientos."},{"heading":"Puntos a destacar","bullets":[{"text":"Política de tratamiento de datos personales"},{"text":"Aviso de privacidad para grupos de interés"},{"text":"Registro Nacional de Bases de Datos (RNBD)"},{"text":"Procedimientos de atención de derechos ARCO"}]}]},{"label":"Capacitación","sections":[{"text":"Formamos al equipo administrativo en sus obligaciones frente al manejo de datos personales."},{"heading":"Puntos a destacar","bullets":[{"text":"Inducción al personal sobre protección de datos"},{"text":"Protocolos de recolección y almacenamiento"},{"text":"Manejo de incidentes de seguridad de datos"},{"text":"Actualizaciones normativas periódicas"}]}]}]'
    FROM dbo.ServiceCategory WHERE Name = N'Propiedades horizontales';
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Service WHERE Slug = N'planes-emergencia')
    INSERT INTO dbo.Service (Slug, CategoryId, Title, Description, HeroImageUrl, Active, Locked, TabsJson)
    SELECT N'planes-emergencia', Id, N'Planes de Emergencia y Contingencia', N'Diseñamos planes de emergencia, evacuación y contingencia para propiedades horizontales, conjuntos residenciales y empresas, cumpliendo la Resolución 0256 de 2014 y la NTC 1700.', N'https://images.unsplash.com/photo-1611348586804-61bf6c080437?w=1400&h=600&fit=crop&auto=format', 1, 1, N'[{"label":"Análisis de riesgo","sections":[{"text":"Realizamos el análisis de amenazas, vulnerabilidades y riesgos específicos de su instalación para fundamentar el plan."},{"heading":"Puntos a destacar","bullets":[{"text":"Identificación de amenazas internas y externas"},{"text":"Análisis de vulnerabilidad estructural"},{"text":"Evaluación de sistemas de detección y control"},{"text":"Mapa de riesgos de la instalación"}]}]},{"label":"Plan de emergencias","sections":[{"text":"Diseñamos el plan de emergencias y evacuación con procedimientos claros, responsables definidos y recursos necesarios."},{"heading":"Puntos a destacar","bullets":[{"text":"Organización de la respuesta a emergencias"},{"text":"Rutas de evacuación y puntos de encuentro"},{"text":"Procedimientos por tipo de emergencia"},{"text":"Inventario de recursos y equipos"}]}]},{"label":"Simulacros","sections":[{"text":"Planificamos y ejecutamos simulacros de evacuación para verificar la efectividad del plan y entrenar a los ocupantes."},{"heading":"Puntos a destacar","bullets":[{"text":"Planificación del simulacro"},{"text":"Ejecución y medición de tiempos"},{"text":"Evaluación y retroalimentación"},{"text":"Actualización del plan según resultados"}]}]}]'
    FROM dbo.ServiceCategory WHERE Name = N'Propiedades horizontales';
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Service WHERE Slug = N'supervision-interventoria')
    INSERT INTO dbo.Service (Slug, CategoryId, Title, Description, HeroImageUrl, Active, Locked, TabsJson)
    SELECT N'supervision-interventoria', Id, N'Supervisión e Interventoría', N'Prestamos servicios de supervisión técnica, administrativa y financiera, e interventoría integral —técnica, administrativa, financiera y jurídica— de proyectos de obra civil e infraestructura, garantizando el cumplimiento del objeto contractual y velando por los intereses del contratante en cada etapa del proyecto.', N'/assets/programacion.jpg', 1, 1, N'[{"label":"Gestión","sections":[{"boxed":true,"text":"En Lidessa, nos comprometemos a ofrecer servicios de supervisión e interventoría de alta calidad, alineados con las normativas legales vigentes y las mejores prácticas en gestión de proyectos. Este servicio está diseñado para garantizar el cumplimiento de los objetivos de los contratos, supervisando cada etapa, con el fin de asegurar eficiencia, calidad, y sostenibilidad."},{"text":"El servicio de interventoría de Lidessa está orientado a acompañar y supervisar proyectos en diversas etapas de su ciclo de vida, asegurando el cumplimiento de los términos contractuales, normativos y técnicos y cubre aspectos esenciales como:"},{"heading":"Gestión Ambiental y Social:","bullets":[{"text":"Aseguramos que las actividades del proyecto respeten las normativas ambientales y promuevan prácticas sostenibles."},{"text":"Realizamos seguimiento a los impactos sociales, minimizando riesgos y promoviendo relaciones positivas con las comunidades involucradas."}]},{"heading":"Gestión Contractual:","bullets":[{"text":"Verificamos el cumplimiento de las obligaciones contractuales por parte de los contratistas."},{"text":"Emitimos informes periódicos que reflejan el estado del proyecto y ofrecen recomendaciones para la mejora continua."}]},{"heading":"Mitigación de Riesgos:","bullets":[{"text":"Identificamos riesgos potenciales en el desarrollo del proyecto y proponemos medidas correctivas oportunas."}]}]},{"label":"Beneficios","sections":[{"heading":"Beneficios del Servicio:","bullets":[{"label":"Garantía de Calidad:","text":"Supervisamos cada detalle del proyecto para cumplir con estándares de excelencia."},{"label":"Eficiencia:","text":"Optimizamos recursos, tiempo y presupuesto."},{"label":"Cumplimiento Normativo:","text":"Aseguramos que las actividades cumplan con las normativas locales e internacionales."},{"label":"Transparencia:","text":"Ofrecemos informes detallados que promueven la confianza entre las partes interesadas."},{"label":"Sostenibilidad:","text":"Fomentamos prácticas responsables que minimicen impactos negativos."}]}]},{"label":"Sectores","sections":[{"heading":"Sectores de Aplicación:","text":"Este servicio está diseñado para adaptarse a proyectos en sectores como:","bullets":[{"text":"Construcción"},{"text":"Infraestructura"},{"text":"Energía"},{"text":"Medio Ambiente"},{"text":"Entre otros"}]},{"heading":"¿Por qué elegir Lidessa?"},{"boxed":true,"text":"Nuestra experiencia y el respaldo de un equipo altamente calificado nos permiten ofrecer un servicio integral que aporta valor a los proyectos de nuestros clientes. En Lidessa, entendemos que cada proyecto es único, y trabajamos en estrecha colaboración con nuestros clientes para garantizar resultados exitosos."}]}]'
    FROM dbo.ServiceCategory WHERE Name = N'Supervisión e interventoría';
GO

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
INSERT INTO dbo.SiteSettings (Id, Phone, Email, Address, Schedule)
VALUES (1, N'+57 333 237 1006', N'administrativo@lidessa.com',
        N'Cra. 71 #46-28, Laureles, Medellín, Antioquia', N'Lunes – Viernes: 7:00 a.m. – 4:30 p.m.');
GO
