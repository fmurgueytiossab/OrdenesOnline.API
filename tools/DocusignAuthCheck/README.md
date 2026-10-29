# Prueba DocuSign en desarrollo

Autenticacion y acceso a cuenta:

```powershell
dotnet run --project .\tools\DocusignAuthCheck\DocusignAuthCheck.csproj
```

Prueba de plantilla y envio por WhatsApp:

```powershell
dotnet run --project .\tools\DocusignAuthCheck\DocusignAuthCheck.csproj -- --send-test
```

Solicita los identificadores de DocuSign, la ruta local del archivo privado RSA,
el caso (1 individual o 2 mancomunada) y los nombres y celulares de prueba.
No guarda credenciales ni imprime tokens. Solo usa el servidor demo.
Antes del envio muestra un resumen y solicita escribir `ENVIAR`.

- Individual: plantilla `a412ccfb-74da-4a7d-b831-a3c354f10c41`, funcion `Cliente`.
- Mancomunada: plantilla `10c7cb91-971e-4d02-aef9-54c5dea3a270`, funciones `Titular1` y `Titular2`.
- Todos los firmantes reciben su enlace personal al mismo documento, en paralelo.
- La mancomunada requiere una firma obligatoria por cada titular.
- Los campos de operacion se completan con datos ficticios y se bloquean.
- La plantilla no debe tener destinatarios adicionales ni funciones de firma incrustada.

Consultar un sobre existente sin reenviar:

```powershell
dotnet run --project .\tools\DocusignAuthCheck\DocusignAuthCheck.csproj -- --status ID_DEL_SOBRE
```

Una respuesta de envio correcta no demuestra entrega en WhatsApp ni firma.
El estado `completed` indica que todas las acciones requeridas terminaron.
Ante un timeout de envio, revisa Acuerdos en DocuSign antes de repetir: el sobre
puede haberse creado. La utilidad no reintenta automaticamente el POST.

## Descarga directa desde el API

La utilidad conserva autenticacion, prueba de envio y consulta de estado.
Se retiraron --download y --watch y el archivado de PDF/metadatos en disco.
Para descargar usar GET /api/Propuesta/{propuestaId}/documento con el JWT del
representante y el backend actualizado. La descarga depende de DocuSign y solo
se permite cuando el sobre vigente tiene todas las firmas completas.

Comprobaciones locales con un servidor simulado, sin credenciales ni envios:

```powershell
dotnet run --project .\tools\DocusignTemplateTestChecks\DocusignTemplateTestChecks.csproj
```
