# Propuestas de clientes con DocuSign

`POST /api/PropuestaCliente` conserva autenticacion, acceso del representante al
cliente, mercados y vigencia. Ahora prepara un sobre DocuSign con los valores de
la propuesta y solicita firma por WhatsApp. `CorreoCliente` es opcional y no se
usa para enviar. No genera un token de aceptacion por correo para nuevas propuestas.

La cartera del representante se resuelve como
`UserRepresentante.RepresentanteId -> CodeRepresentante.Cosabcli -> clientes.cosabcli -> clientes.gestor`.
Se buscan clientes activos con esos gestores. El codigo del representante no se
usa directamente como gestor; si su fila en `clientes` no tiene gestor, no se
obtiene una cartera. En el modelo actual los codigos estan en `CodeRepresentante`,
asociados a `UserRepresentante` por `RepresentanteId`.

Los nombres y celulares proceden de Opersab:

- Individual: nombre formado por `clientes.nombres + apepat + apemat` y celular
  `clientes.nucel`, funcion `Cliente`. Se omiten partes vacias y se recortan espacios;
  `descli` no es obligatorio para la persona natural.
- Mancomunada (`fg_mancomunado` S/M): dos filas de `clientes_apoderados` con el mismo
  `cosabcli` de la cuenta. Se usa `clientes.descli` en ambas y el `nucel` de cada fila.
  Funciones `Titular1` y `Titular2`, dos celulares validos y distintos. Ambos deben
  firmar el mismo sobre. El orden de asignacion es estable por celular; el codigo
  compartido no elimina ninguna fila. No se seleccionan arbitrariamente dos filas
  si hay mas de dos apoderados: la solicitud devuelve 400 para corregir los datos.

El celular puede venir con nueve digitos de Peru o prefijo 51/051, con espacios,
parentesis o guiones. Se guarda normalizado como `+51999999999`.

## Configuracion

Ejecutar `20261005_propuesta_docusign.sql` en **DefaultConnection**, no Opersab.
No se modifican los modelos ni los contextos EF existentes; el repositorio accede
a esas tablas por SQL parametrizado.

Variables del proceso de la API (en IIS deben existir en el servidor):

```text
DocuSign__IntegrationKey
DocuSign__UserId
DocuSign__AccountId
DocuSign__AuthServer=account-d.docusign.com
DocuSign__PrivateKeyPath
DocuSign__IndividualTemplateId=a412ccfb-74da-4a7d-b831-a3c354f10c41
DocuSign__JointTemplateId=10c7cb91-971e-4d02-aef9-54c5dea3a270
DocuSign__IndividualRole=Cliente
DocuSign__JointFirstRole=Titular1
DocuSign__JointSecondRole=Titular2
DocuSign__SignedDocumentsPath=C:\Users\Fernando\Music\docusign
DocuSign__WebhookUrl=https://ordenestest.seminariosab.com.pe/api/DocuSign/webhook
DocuSign__WebhookHmacSecret
```

`DocuSign__Environment` no se utiliza. El entorno SQL se deriva del servidor OAuth:
`account-d.docusign.com` -> Desarrollo, `account.docusign.com` -> Produccion.
La URL API se obtiene de OAuth/userinfo para la cuenta configurada; no se fija un
servidor de produccion. JWT/RSA/HMAC no se almacenan en las tablas ni se escriben
en los logs. El Application Pool necesita lectura sobre la clave privada y
escritura sobre la carpeta de documentos. Reiniciar la aplicacion tras cambiar
variables. Mantener ASPNETCORE_ENVIRONMENT=Test para este despliegue.

Cada funcion necesita al menos una firma obligatoria de firma remota y las
etiquetas de texto deben coincidir exactamente con estas:

```text
Operacion Valor Cantidad TipoOrden Precio Moneda Monto Vigencia Mercado
NombreRepresentante CodigoCliente
```

Los textos se rellenan desde el endpoint y quedan bloqueados para el firmante.

## Envio y respuesta

Primero se crea un borrador en DocuSign, se registran sobre y firmantes juntos
en SQL y finalmente se envia. Si SQL falla, el borrador no se envia. Un timeout
no produce un reenvio automatico. Si devuelve advertencia, revisar el sobre en
Acuerdos antes de repetir para evitar propuestas o envios duplicados.

La respuesta 201 agrega `envelopeId` y `docuSignEnviado`. Este ultimo indica que
DocuSign acepto el envio, no que WhatsApp lo entrego ni que ya esta firmado.
`emailDelivered` se conserva por compatibilidad y vale false. Si una propuesta
se guardo pero no se confirmo el envio, se devuelve `docuSignEnviado=false` y una
advertencia; la propuesta queda pendiente.

## Webhook y archivo firmado

Publicar y activar la configuracion **Connect a nivel de cuenta** en la URL
`POST /api/DocuSign/webhook`, con REST v2.1, SIM, firma HMAC y los eventos de sobre
y destinatario configurados. No agregar otra notificacion por sobre. No se
necesita incluir los documentos en el mensaje de Connect: se descargan por API.
La API local con `dotnet run` no recibe notificaciones dirigidas al servidor IIS.

El endpoint no exige JWT de la aplicacion: verifica HMAC sobre los bytes originales
del cuerpo con los encabezados `X-Docusign-Signature-*`. HMAC invalido devuelve
401; cuerpo invalido 400; errores de persistencia/consulta devuelven 503 para
reintento de Connect. Sobres no registrados o de otra cuenta se ignoran con 200.
Consultar el estado actual de DocuSign evita actualizar con el estado de una
notificacion atrasada. SQL conserva estados terminales y solo actualiza el sobre
vigente. Los antiguos endpoints de revision no aceptan propuestas con DocuSign.

La propuesta pasa a `Aceptado` solo con sobre `completed` y todos los destinatarios
registrados `completed`; `declined`/`voided` pasan a `Cancelado`. La firma del primer
titular por si sola deja la propuesta pendiente. Esto no confirma ejecucion de la orden.

Un servicio en segundo plano revisa descargas pendientes cada minuto, vuelve a
verificar que el sobre este completo y descarga PDF combinado con certificado.
Guarda `<PropuestaId>.pdf` en `DocuSign__SignedDocumentsPath`, registra SHA-256,
ubicacion y fecha en SQL, y un pequeno archivo `.docusign.json` para recuperar
interrupciones. No sobrescribe otro PDF ni uno cuya huella haya cambiado. Limite
50 MB por documento. La descarga puede tardar mas de un minuto segun red y cola.

Reintenta hasta diez fallos. Si `EstadoDescarga='Error'`, revisar permisos,
conectividad, configuracion y conflictos de archivos antes de habilitar otros
intentos para ese registro. Reiniciar IIS no pierde descargas ya persistidas.
En IIS se recomienda mantener el proceso activo (AlwaysRunning/preload segun
la configuracion del servidor) para que el worker ejecute sin esperar trafico.

## Verificacion

```powershell
dotnet test .\OrdenesOnline.Tests\OrdenesOnline.Tests.csproj --no-restore
dotnet run --project .\OrdenesOnline-API\OrdenesOnline-API.csproj --launch-profile test
```

Tras publicar, activar Connect y realizar una propuesta individual y una
mancomunada por Swagger con la autenticacion de representante. Verificar en
`PropuestaDocuSign`, `PropuestaDocuSignFirmante` y `propuesta` que el sobre mancomunado
permanezca pendiente despues de la primera firma, y que despues de la segunda
quede aceptado y archivado. Los sobres previos creados por la herramienta de
prueba no se vinculan automaticamente a propuestas reales.

Las pruebas automatizadas usan HTTP simulado, RSA temporal y archivos temporales:
no envian mensajes ni firman documentos reales. La verificacion de tablas SQL,
entrega WhatsApp y webhook publico requiere la prueba de integracion en Test.

Referencia para validacion del cuerpo original:
[DocuSign HMAC](https://www.docusign.com/blog/developers/manually-authenticating-hmac-signatures-docusign-connect-webhook-configurations).
