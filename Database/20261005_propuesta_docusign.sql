/*
    SQL Server - Sobres y firmantes de DocuSign asociados a propuestas.
    Ejecutar en la base de datos de OrdenesOnline (DefaultConnection), no Opersab.
    No modifica dbo.propuesta ni ejecuta envios a DocuSign.

    Relacion: propuesta 1:N PropuestaDocuSign 1:N PropuestaDocuSignFirmante.
    Una propuesta tiene un solo sobre vigente; los anteriores se conservan.
    Mancomunada: un sobre y dos firmantes obligatorios, no dos sobres.
    Claves RSA, secretos y tokens NO se almacenan en estas tablas.

    Si las tablas ya existen, se conservan sin alterar su estructura.
    La aplicacion debe registrar exactamente FirmasRequeridas firmantes y
    procesar solo el sobre vigente al actualizar el estado de la propuesta.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.propuesta', N'U') IS NULL
    THROW 51000, 'No existe dbo.propuesta. Selecciona la base de datos correcta.', 1;

IF NOT EXISTS (
    SELECT 1
    FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.propuesta', N'U')
      AND name = N'PropuestaId'
      AND system_type_id = TYPE_ID(N'int')
)
    THROW 51001, 'dbo.propuesta.PropuestaId no existe o no es int. Revisa el esquema antes de ejecutar.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.PropuestaDocuSign', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.PropuestaDocuSign
        (
            PropuestaDocuSignId bigint IDENTITY(1,1) NOT NULL,
            PropuestaId int NOT NULL,
            EnvelopeId uniqueidentifier NOT NULL,
            AccountId uniqueidentifier NOT NULL,
            Entorno varchar(20) NOT NULL,
            TemplateId uniqueidentifier NOT NULL,
            EstadoSobre varchar(30) NOT NULL,
            FirmasRequeridas tinyint NOT NULL,
            EsVigente bit NOT NULL
                CONSTRAINT DF_PropuestaDocuSign_EsVigente DEFAULT (1),
            FechaRegistroUtc datetime2(3) NOT NULL
                CONSTRAINT DF_PropuestaDocuSign_FechaRegistroUtc DEFAULT (SYSUTCDATETIME()),
            FechaEnvioUtc datetime2(3) NULL,
            FechaCompletadoUtc datetime2(3) NULL,
            FechaEstadoUtc datetime2(3) NULL,
            EstadoDescarga varchar(20) NOT NULL
                CONSTRAINT DF_PropuestaDocuSign_EstadoDescarga DEFAULT ('Pendiente'),
            UbicacionArchivo nvarchar(1000) NULL,
            Sha256Archivo char(64) NULL,
            FechaDescargaUtc datetime2(3) NULL,
            IntentosDescarga int NOT NULL
                CONSTRAINT DF_PropuestaDocuSign_IntentosDescarga DEFAULT (0),
            UltimoError nvarchar(2000) NULL,
            VersionFila rowversion NOT NULL,

            CONSTRAINT PK_PropuestaDocuSign
                PRIMARY KEY (PropuestaDocuSignId),
            CONSTRAINT FK_PropuestaDocuSign_Propuesta
                FOREIGN KEY (PropuestaId) REFERENCES dbo.propuesta (PropuestaId),
            CONSTRAINT CK_PropuestaDocuSign_Entorno
                CHECK (Entorno IN ('Desarrollo', 'Produccion')),
            CONSTRAINT CK_PropuestaDocuSign_FirmasRequeridas
                CHECK (FirmasRequeridas IN (1, 2)),
            CONSTRAINT CK_PropuestaDocuSign_IntentosDescarga
                CHECK (IntentosDescarga >= 0),
            CONSTRAINT CK_PropuestaDocuSign_EstadoDescarga
                CHECK (EstadoDescarga IN ('Pendiente', 'Descargado', 'Error')),
            CONSTRAINT CK_PropuestaDocuSign_Sha256
                CHECK (Sha256Archivo IS NULL OR
                    (LEN(Sha256Archivo) = 64 AND
                     Sha256Archivo COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9A-Fa-f]%')),
            CONSTRAINT CK_PropuestaDocuSign_Descargado
                CHECK (EstadoDescarga <> 'Descargado' OR
                    (EstadoSobre = 'completed' AND
                     UbicacionArchivo IS NOT NULL AND LEN(LTRIM(RTRIM(UbicacionArchivo))) > 0 AND
                     Sha256Archivo IS NOT NULL AND FechaDescargaUtc IS NOT NULL))
        );

        CREATE UNIQUE INDEX UX_PropuestaDocuSign_Sobre
            ON dbo.PropuestaDocuSign (Entorno, AccountId, EnvelopeId);

        CREATE UNIQUE INDEX UX_PropuestaDocuSign_PropuestaVigente
            ON dbo.PropuestaDocuSign (PropuestaId)
            WHERE EsVigente = 1;

        CREATE INDEX IX_PropuestaDocuSign_Historial
            ON dbo.PropuestaDocuSign (PropuestaId, FechaRegistroUtc DESC);

        CREATE INDEX IX_PropuestaDocuSign_Descargas
            ON dbo.PropuestaDocuSign (EstadoSobre, EstadoDescarga, FechaEstadoUtc)
            INCLUDE (PropuestaId, EnvelopeId, AccountId, Entorno, IntentosDescarga);
    END;

    IF OBJECT_ID(N'dbo.PropuestaDocuSignFirmante', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.PropuestaDocuSignFirmante
        (
            PropuestaDocuSignFirmanteId bigint IDENTITY(1,1) NOT NULL,
            PropuestaDocuSignId bigint NOT NULL,
            RecipientId varchar(64) NOT NULL,
            Funcion varchar(30) NOT NULL,
            IdentificacionTitular nvarchar(100) NULL,
            Nombre nvarchar(200) NOT NULL,
            Celular varchar(20) NOT NULL,
            EstadoFirmante varchar(30) NOT NULL,
            FechaFirmaUtc datetime2(3) NULL,
            FechaEstadoUtc datetime2(3) NULL,
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
                CHECK (Funcion IN ('Cliente', 'Titular1', 'Titular2')),
            CONSTRAINT CK_PropuestaDocuSignFirmante_Recipient
                CHECK (LEN(LTRIM(RTRIM(RecipientId))) > 0),
            CONSTRAINT CK_PropuestaDocuSignFirmante_Nombre
                CHECK (LEN(LTRIM(RTRIM(Nombre))) > 0),
            CONSTRAINT CK_PropuestaDocuSignFirmante_Celular
                CHECK (LEN(LTRIM(RTRIM(Celular))) > 0)
        );
    END;

    COMMIT TRANSACTION;
    PRINT 'Tablas de DocuSign disponibles. No se modificaron propuestas existentes.';
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;

/*
    Notas de integracion:
    - Guardar Celular normalizado con codigo de pais, por ejemplo +51999999999.
    - EstadoSobre y EstadoFirmante conservan los valores confirmados por DocuSign.
    - Descargar solo cuando EstadoSobre = 'completed'; reintentar errores de archivo.
    - Guardar UbicacionArchivo desde configuracion, no una ruta fija en el esquema.
    - El documento firmado se nombra PropuestaId.pdf. Si hay reemplazos, conservar
      los archivos historicos por sobre o version; nunca sobrescribir otro firmado.
    - Para un reemplazo, desactivar el anterior e insertar el nuevo en una transaccion.
    - No almacenar tokens, claves ni cuerpos completos con secretos en UltimoError.
    - Este script no configura webhooks ni agrega repositorios o modelos .NET.
*/
