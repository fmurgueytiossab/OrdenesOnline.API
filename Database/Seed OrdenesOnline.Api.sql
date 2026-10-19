/*
    Dataset inicial facilitado para OrdenesOnline.API. Ejecutar despues del Create.
    Conserva los IDs 1 y 33 para que las asociaciones sean las indicadas.
    Admite repeticion sin duplicar usuarios/asociaciones ni cambiar contrasenas existentes.
    Los bytes cifrados requieren DATAWEB_KEY original y su certificado/cadena de proteccion.
    No crean claves ni prueban que una clave nueva pueda descifrarlos.
*/
SET XACT_ABORT ON;
IF OBJECT_ID(N'dbo.UserRepresentante',N'U') IS NULL OR OBJECT_ID(N'dbo.CodeRepresentante',N'U') IS NULL
    THROW 51020, 'Ejecuta primero Create OrdenesOnline.Api.sql en la base correcta.', 1;

BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @Password VARBINARY(MAX) = 0x0006D7727CBFB94C91152C9B9226535E02000000732F92841814C8E5EF01B3F95346DB7DF366AD19ABF9EAB472F90D2EB8FE2485F19A2F6E755D83925F02EB92CBC831B4;
    DECLARE @Dataset TABLE
    (
        RepresentanteId INT PRIMARY KEY, Correo_Corporativo VARCHAR(100),
        Nombre VARCHAR(100), Dni VARCHAR(8), Cosabcli CHAR(6)
    );
    INSERT INTO @Dataset (RepresentanteId,Correo_Corporativo,Nombre,Dni,Cosabcli) VALUES
        (1,'fmurgueytio@seminariosab.com.pe','Fernando Murgueytio','72372488','026775'),
        (33,'jcuzzi@seminariosab.com.pe','Javier Cuzzi','87095503','006186');

    IF EXISTS (
        SELECT 1 FROM dbo.UserRepresentante u JOIN @Dataset s
            ON u.RepresentanteId=s.RepresentanteId OR u.Correo_Corporativo=s.Correo_Corporativo
        WHERE u.RepresentanteId<>s.RepresentanteId OR u.Correo_Corporativo<>s.Correo_Corporativo
    )
        THROW 51021, 'Un ID o correo del dataset pertenece a otro usuario. No se modifico la instalacion.', 1;

    SET IDENTITY_INSERT dbo.UserRepresentante ON;
    INSERT INTO dbo.UserRepresentante (RepresentanteId,Correo_Corporativo,[Password],Nombre,Dni)
        SELECT s.RepresentanteId,s.Correo_Corporativo,@Password,s.Nombre,s.Dni FROM @Dataset s
        WHERE NOT EXISTS (SELECT 1 FROM dbo.UserRepresentante u WHERE u.RepresentanteId=s.RepresentanteId);
    SET IDENTITY_INSERT dbo.UserRepresentante OFF;

    INSERT INTO dbo.CodeRepresentante (RepresentanteId,Cosabcli)
        SELECT s.RepresentanteId,s.Cosabcli FROM @Dataset s
        WHERE NOT EXISTS (SELECT 1 FROM dbo.CodeRepresentante c
            WHERE c.RepresentanteId=s.RepresentanteId AND c.Cosabcli=s.Cosabcli);
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    SET IDENTITY_INSERT dbo.UserRepresentante OFF;
    THROW;
END CATCH;
