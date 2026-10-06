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

## Archivo firmado

Descarga un sobre existente si su estado es `completed`:

```powershell
dotnet run --project .\tools\DocusignAuthCheck\DocusignAuthCheck.csproj -- --download ID_DEL_SOBRE NUMERO_PROPUESTA
```

Para esperar y descargar automaticamente al completarse, deja este comando abierto:

```powershell
dotnet run --project .\tools\DocusignAuthCheck\DocusignAuthCheck.csproj -- --watch ID_DEL_SOBRE NUMERO_PROPUESTA
```

Consulta inmediatamente y luego cada 15 minutos, renovando la autenticacion.
No ejecuta consultas continuas en intervalos menores, conforme a las reglas de
polling de DocuSign. Ctrl+C detiene la espera. Puedes reanudar con el mismo comando.
Los sobres rechazados o anulados no se descargan.

Por defecto guarda `NUMERO_PROPUESTA.pdf` y `NUMERO_PROPUESTA.docusign.json` en
`C:\Users\Fernando\Music\docusign`. Solicita el PDF combinado con el certificado
de finalizacion (`documents/combined?certificate=true`). El JSON contiene relacion,
estado, fechas y SHA-256; no credenciales. La validacion de formato solo comprueba
la cabecera PDF, no la validez criptografica de las firmas.

La ruta se puede cambiar para esta terminal:

```powershell
$env:DOCUSIGN_SIGNED_DOCUMENTS_PATH = 'D:\DocumentosFirmados'
```

No sobrescribe PDFs existentes. Si el mismo sobre ya esta archivado, verifica la
huella y reutiliza el archivo. Un numero ligado a otro sobre produce conflicto.
La prueba no genera propuestas en SQL: el numero se proporciona manualmente.
Cuando se integre el servicio se usara el `PropuestaId` generado por la base.
El seguimiento actual funciona mientras este proceso esta abierto; el proceso
del servidor se conectara posteriormente a DocuSign Connect con reintentos de archivo.

Comprobaciones locales con un servidor simulado, sin credenciales ni envios:

```powershell
dotnet run --project .\tools\DocusignTemplateTestChecks\DocusignTemplateTestChecks.csproj
```

Esta utilidad aun no reemplaza el correo de `PropuestaClienteService`, no lee
clientes de la base de datos ni actualiza propuestas. La integracion del servicio,
la persistencia de la relacion propuesta/sobre y la recepcion segura de eventos
de DocuSign son el siguiente paso despues de verificar ambos casos en demo.
