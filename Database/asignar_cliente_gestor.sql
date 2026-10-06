/*
    Ejecutar en la base configurada como ConnectionStrings:Opersab del ambiente Test.
    Cambia solamente el gestor del cliente indicado al gestor obtenido buscando
    el cosabcli del representante en clientes. Cosabcli y gestor pueden ser distintos.
    Requisito: 026775 debe estar vinculado al representante conectado en
    CodeRepresentante, en la base de OrdenesOnline (DefaultConnection).
    La visibilidad se comparte con otros representantes que resuelvan ese gestor.
    Este script no modifica estado, celulares ni CodeRepresentante.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @CodigoReferencia varchar(30) = '026775';
DECLARE @CodigoCliente varchar(30) = 'REEMPLAZAR_CODIGO';

IF @CodigoCliente = 'REEMPLAZAR_CODIGO' OR LEN(LTRIM(RTRIM(@CodigoCliente))) = 0
    THROW 51000, 'Reemplaza @CodigoCliente por el cosabcli real del cliente.', 1;

IF @CodigoCliente = @CodigoReferencia
    THROW 51001, 'El cliente destino debe ser distinto del codigo de referencia.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    IF (SELECT COUNT(*) FROM dbo.clientes WITH (UPDLOCK,HOLDLOCK)
        WHERE cosabcli = @CodigoReferencia) <> 1
        THROW 51002, 'El codigo de referencia no identifica una sola fila en clientes.', 1;

    IF EXISTS (SELECT 1 FROM dbo.clientes WHERE cosabcli = @CodigoReferencia
               AND (gestor IS NULL OR LEN(LTRIM(RTRIM(gestor))) = 0))
        THROW 51003, 'La fila del representante en clientes no tiene gestor. Revisa su asignacion antes de cambiar otros clientes.', 1;

    IF (SELECT COUNT(*) FROM dbo.clientes WITH (UPDLOCK,HOLDLOCK)
        WHERE cosabcli = @CodigoCliente) <> 1
        THROW 51004, 'El cliente destino no identifica una sola fila en clientes.', 1;

    IF EXISTS (SELECT 1 FROM dbo.clientes WHERE cosabcli = @CodigoCliente AND estado = '9')
        THROW 51005, 'El cliente tiene estado 9 y la busqueda lo excluye. No se cambio su estado ni su gestor.', 1;

    UPDATE destino
        SET gestor = origen.gestor
        OUTPUT inserted.cosabcli AS CodigoCliente,
               deleted.gestor AS GestorAnterior,
               inserted.gestor AS GestorNuevo
    FROM dbo.clientes AS destino
    JOIN dbo.clientes AS origen ON origen.cosabcli = @CodigoReferencia
    WHERE destino.cosabcli = @CodigoCliente;

    IF @@ROWCOUNT <> 1
        THROW 51006, 'No se actualizo exactamente un cliente.', 1;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
