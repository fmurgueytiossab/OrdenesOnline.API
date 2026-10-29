/*
    OrdenesOnline.API - esquema consolidado de DefaultConnection.
    Seleccionar primero una base SQL Server vacia destinada al API.
    Este archivo crea seis tablas y cinco procedimientos. No crea bases ni carga datos.
    Rechaza una instalacion que ya contenga cualquiera de estos objetos.
    Todo se crea en una transaccion; no es un script de actualizacion.

    Cifrado de contrasenas: usa DATAWEB_KEY y DATAWEB_CERTIFICATE ya provisionados.
    No inventa ni reemplaza esas claves. Para reutilizar el dataset cifrado se deben
    restaurar los objetos criptograficos originales y su cadena de proteccion.
    Las definiciones de Encrypt/Decrypt son las facilitadas para esta instalacion.
    Opersab es otra conexion: sus tablas no se crean aqui.
*/
SET XACT_ABORT ON;

IF EXISTS (
    SELECT 1 FROM sys.objects o JOIN sys.schemas s ON s.schema_id=o.schema_id
    WHERE s.name=N'dbo' AND o.name IN
        (N'Propuesta',N'UserRepresentante',N'CodeRepresentante',N'Token',
         N'PropuestaDocuSign',N'PropuestaDocuSignFirmante',N'usp_EncryptText',N'usp_DecryptText',
         N'usp_getRepresentantePassword',N'usp_isValidRepresentantePassword',N'usp_updateRepresentantePassword')
)
    THROW 51000, 'Ya existen objetos de OrdenesOnline.API. Este script es de creacion inicial, no de actualizacion.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    CREATE TABLE [dbo].[Propuesta]
    (
        [PropuestaId] [int] IDENTITY(1,1) NOT NULL,
        [Fecha_Registro] [datetime] NOT NULL,
        [Nombre_Operador] [varchar](100) NOT NULL,
        [Correo_Corporativo] [varchar](100) NOT NULL,
        [Cosabcli] [varchar](6) NOT NULL,
        [Tipo] [varchar](6) NOT NULL,
        [Instrumento] [varchar](100) NOT NULL,
        [Tipo_Orden] [varchar](10) NOT NULL,
        [Cantidad] [int] NOT NULL,
        [Precio] [decimal](18,4) NULL,
        [monto] [decimal](18,2) NULL,
        [Vigencia] [varchar](30) NOT NULL,
        [Mercado] [varchar](10) NOT NULL,
        [estado] [varchar](20) NULL,
        CONSTRAINT [PK_Propuesta] PRIMARY KEY CLUSTERED ([PropuestaId] ASC)
    ) ON [PRIMARY];

    CREATE TABLE [dbo].[UserRepresentante]
    (
        [RepresentanteId] [int] IDENTITY(1,1) NOT NULL,
        [Correo_Corporativo] [varchar](100) NOT NULL,
        [Password] [varbinary](max) NOT NULL,
        [Nombre] [varchar](100) NOT NULL,
        [Dni] [varchar](8) NOT NULL,
        CONSTRAINT [PK_UserRepresentante] PRIMARY KEY CLUSTERED ([RepresentanteId] ASC)
    ) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY];

    CREATE TABLE [dbo].[CodeRepresentante]
    (
        [RepresentanteId] [int] NOT NULL,
        [Cosabcli] [char](6) NOT NULL,
        CONSTRAINT [PK_RepresentanteCode] PRIMARY KEY CLUSTERED ([RepresentanteId] ASC,[Cosabcli] ASC),
        CONSTRAINT [FK_RepresentanteCode_User] FOREIGN KEY ([RepresentanteId])
            REFERENCES [dbo].[UserRepresentante] ([RepresentanteId])
    ) ON [PRIMARY];

    -- Requerida por ActionTokenRepository: recuperacion de contrasena y revision por token.
    CREATE TABLE [dbo].[Token]
    (
        [TokenId] [int] IDENTITY(1,1) NOT NULL,
        [UserId] [int] NOT NULL,
        [PropuestaId] [int] NULL,
        [TokenHash] [char](64) NOT NULL,
        [Type] [varchar](20) NOT NULL,
        [CreatedAt] [datetime2](3) NOT NULL,
        [ExpiresAt] [datetime2](3) NOT NULL,
        [IsUsed] [bit] NOT NULL CONSTRAINT [DF_Token_IsUsed] DEFAULT (0),
        [IsRevoked] [bit] NOT NULL CONSTRAINT [DF_Token_IsRevoked] DEFAULT (0),
        CONSTRAINT [PK_Token] PRIMARY KEY CLUSTERED ([TokenId]),
        CONSTRAINT [FK_Token_UserRepresentante] FOREIGN KEY ([UserId]) REFERENCES [dbo].[UserRepresentante] ([RepresentanteId]),
        CONSTRAINT [FK_Token_Propuesta] FOREIGN KEY ([PropuestaId]) REFERENCES [dbo].[Propuesta] ([PropuestaId]),
        CONSTRAINT [CK_Token_Type] CHECK ([Type] IN ('proposal_review','password_reset'))
    );
    CREATE UNIQUE INDEX [IX_Token_TokenHash] ON [dbo].[Token] ([TokenHash]);
    CREATE INDEX [IX_Token_UserId_Type] ON [dbo].[Token] ([UserId],[Type]);

    CREATE TABLE dbo.PropuestaDocuSign
    (
        PropuestaDocuSignId bigint IDENTITY(1,1) NOT NULL,
        PropuestaId int NOT NULL,
        EnvelopeId uniqueidentifier NOT NULL,
        AccountId uniqueidentifier NOT NULL,
        TemplateId uniqueidentifier NOT NULL,
        EstadoSobre varchar(30) NOT NULL,
        FirmasRequeridas tinyint NOT NULL,
        EsVigente bit NOT NULL
            CONSTRAINT DF_PropuestaDocuSign_EsVigente DEFAULT (1),
        FechaRegistroUtc datetime2(3) NOT NULL
            CONSTRAINT DF_PropuestaDocuSign_FechaRegistroUtc DEFAULT (SYSUTCDATETIME()),
        FechaEnvioUtc datetime2(3) NULL,
        FechaCompletadoUtc datetime2(3) NULL,
        FechaCambioEstadoUtc datetime2(3) NULL,
        VersionFila rowversion NOT NULL,

        CONSTRAINT PK_PropuestaDocuSign
            PRIMARY KEY (PropuestaDocuSignId),
        CONSTRAINT FK_PropuestaDocuSign_Propuesta
            FOREIGN KEY (PropuestaId) REFERENCES dbo.propuesta (PropuestaId),
        CONSTRAINT CK_PropuestaDocuSign_FirmasRequeridas
            CHECK (FirmasRequeridas IN (1, 2, 3))
    );

    CREATE UNIQUE INDEX UX_PropuestaDocuSign_Sobre
        ON dbo.PropuestaDocuSign (AccountId, EnvelopeId);

    CREATE UNIQUE INDEX UX_PropuestaDocuSign_PropuestaVigente
        ON dbo.PropuestaDocuSign (PropuestaId)
        WHERE EsVigente = 1;

    CREATE INDEX IX_PropuestaDocuSign_Historial
        ON dbo.PropuestaDocuSign (PropuestaId, FechaRegistroUtc DESC);

    CREATE TABLE dbo.PropuestaDocuSignFirmante
    (
        PropuestaDocuSignFirmanteId bigint IDENTITY(1,1) NOT NULL,
        PropuestaDocuSignId bigint NOT NULL,
        RecipientId varchar(64) NOT NULL,
        Funcion varchar(30) NOT NULL,
        Nombre nvarchar(200) NOT NULL,
        Celular varchar(20) NOT NULL,
        EstadoFirmante varchar(30) NOT NULL,
        FechaFirmaUtc datetime2(3) NULL,
        FechaRegistroUtc datetime2(3) NOT NULL
            CONSTRAINT DF_PropuestaDocuSignFirmante_FechaRegistroUtc DEFAULT (SYSUTCDATETIME()),
        VersionFila rowversion NOT NULL,

        CONSTRAINT PK_PropuestaDocuSignFirmante
            PRIMARY KEY (PropuestaDocuSignFirmanteId),
        CONSTRAINT FK_PropuestaDocuSignFirmante_Sobre
            FOREIGN KEY (PropuestaDocuSignId)
            REFERENCES dbo.PropuestaDocuSign (PropuestaDocuSignId),
        CONSTRAINT UQ_PropuestaDocuSignFirmante_Recipient
            UNIQUE (PropuestaDocuSignId, RecipientId),
        CONSTRAINT UQ_PropuestaDocuSignFirmante_Funcion
            UNIQUE (PropuestaDocuSignId, Funcion),
        CONSTRAINT CK_PropuestaDocuSignFirmante_Funcion
            CHECK (Funcion IN ('Cliente', 'Titular1', 'Titular2', 'Titular3')),
        CONSTRAINT CK_PropuestaDocuSignFirmante_Recipient
            CHECK (LEN(LTRIM(RTRIM(RecipientId))) > 0),
        CONSTRAINT CK_PropuestaDocuSignFirmante_Nombre
            CHECK (LEN(LTRIM(RTRIM(Nombre))) > 0),
        CONSTRAINT CK_PropuestaDocuSignFirmante_Celular
            CHECK (LEN(LTRIM(RTRIM(Celular))) > 0)
    );


    -- SQL dinamico mantiene CREATE PROCEDURE como primer comando de su lote,
    -- sin GO y dentro de la misma transaccion de instalacion.
    EXEC(N'CREATE PROCEDURE [dbo].[usp_EncryptText]
    @TextToEncrypt NVARCHAR(MAX), @EncryptedText VARBINARY(MAX) OUTPUT
AS
BEGIN
    OPEN SYMMETRIC KEY DATAWEB_KEY DECRYPTION BY CERTIFICATE DATAWEB_CERTIFICATE;
    SET @EncryptedText = ENCRYPTBYKEY(KEY_GUID(''DATAWEB_KEY''), @TextToEncrypt);
    CLOSE SYMMETRIC KEY DATAWEB_KEY;
END;');

    EXEC(N'CREATE PROCEDURE [dbo].[usp_DecryptText]
    @EncryptedText VARBINARY(MAX), @decryptedText NVARCHAR(MAX) OUTPUT
AS
BEGIN
    OPEN SYMMETRIC KEY DATAWEB_KEY DECRYPTION BY CERTIFICATE DATAWEB_CERTIFICATE;
    SET @decryptedText = CONVERT(NVARCHAR(MAX), DECRYPTBYKEY(@EncryptedText));
    CLOSE SYMMETRIC KEY DATAWEB_KEY;
END;');

    EXEC(N'CREATE PROCEDURE [dbo].[usp_getRepresentantePassword] @correo VARCHAR(50)
AS
BEGIN
    DECLARE @encryptedPassword VARBINARY(MAX), @decryptedPassword NVARCHAR(MAX);
    SELECT @encryptedPassword = [Password] FROM dbo.UserRepresentante WHERE Correo_Corporativo=@correo;
    EXECUTE dbo.usp_DecryptText @encryptedPassword, @decryptedPassword OUTPUT;
    SELECT @decryptedPassword;
END;');

    EXEC(N'CREATE PROCEDURE [dbo].[usp_isValidRepresentantePassword] @correo VARCHAR(50), @password VARCHAR(100)
AS
BEGIN
    DECLARE @encryptedPassword VARBINARY(MAX), @decryptedPassword NVARCHAR(20);
    SELECT @encryptedPassword = [Password] FROM dbo.UserRepresentante WHERE Correo_Corporativo=@correo;
    EXECUTE dbo.usp_DecryptText @encryptedPassword, @decryptedPassword OUTPUT;
    SELECT IsValid = CASE WHEN @decryptedPassword=@password THEN 1 ELSE 0 END, RepresentanteId
    FROM dbo.UserRepresentante WHERE Correo_Corporativo=@correo;
END;');

    EXEC(N'CREATE PROCEDURE [dbo].[usp_updateRepresentantePassword] @correo VARCHAR(50), @password NVARCHAR(MAX)
AS
BEGIN
    DECLARE @encryptedPassword VARBINARY(MAX), @sql NVARCHAR(MAX);
    EXECUTE dbo.usp_EncryptText @password, @encryptedPassword OUTPUT;
    UPDATE dbo.UserRepresentante SET [Password]=@encryptedPassword WHERE Correo_Corporativo=@correo;
    RETURN @@ROWCOUNT;
END;');

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
