# OrdenesOnline.API

API ASP.NET Core para autenticación de representantes, consulta de valores y creación de propuestas.

## Organización

Los controladores se agrupan por capacidad dentro de `OrdenesOnline-API/Features`:

```text
Features/
  Authentication/
  PasswordRecovery/
  Propuestas/
  Valores/
```

Las rutas públicas existentes se mantienen para no romper al frontend:

- `POST /api/Representante/login`
- `POST /api/Representante/update-password`
- `GET /api/Representante/me`
- `POST /api/Email/send-validation`
- `GET /api/Email/validate`
- `POST /api/Propuesta`
- `POST /api/PropuestaCliente`
- `POST /api/PropuestaCliente/revision/validar`
- `POST /api/PropuestaCliente/revision`
- `GET /api/Valor`

Los errores utilizan `ProblemDetails`. Login inválido devuelve `401`, validación incorrecta devuelve `400`, autorización insuficiente devuelve `403` y la creación de una propuesta devuelve `201`.

`POST /api/PropuestaCliente` conserva la validación del representante y del código de cliente. Solo acepta `BVL`, `Canaccord Renta4` o `Pershing` en `mercado`; después de guardar, genera un token de revisión y envía al `correoCliente` un resumen de la operación con el enlace correspondiente. Este flujo no utiliza Zapier.

## Configuración local

Los secretos no deben guardarse en `appsettings*.json`. Para desarrollo se usa el Secret Manager asociado al proyecto:

```powershell
dotnet user-secrets set "Jwt:Key" "<clave-de-al-menos-32-bytes>" --project OrdenesOnline-API
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "<cadena>" --project OrdenesOnline-API
dotnet user-secrets set "ConnectionStrings:Opersab" "<cadena>" --project OrdenesOnline-API
dotnet user-secrets set "App:ZapierWebhookUrl" "<url>" --project OrdenesOnline-API
dotnet user-secrets set "Email:From" "<correo>" --project OrdenesOnline-API
dotnet user-secrets set "Email:User" "<usuario>" --project OrdenesOnline-API
dotnet user-secrets set "Email:Pass" "<contraseña>" --project OrdenesOnline-API
```

Test y Producción deben recibir secretos desde el sistema de despliegue. En variables de entorno, ASP.NET Core representa `:` mediante `__`, por ejemplo:

```text
Jwt__Key
ConnectionStrings__DefaultConnection
ConnectionStrings__Opersab
App__ZapierWebhookUrl
App__ClientesFrontendUrl
Email__From
Email__User
Email__Pass
```

Las credenciales que estuvieron versionadas deben rotarse aunque ya no aparezcan en los archivos actuales, porque siguen existiendo en el historial de Git.

## Tokens

- Los access tokens tienen audiencia `ClientesFrontend`, propósito `access` y duración configurable.
- Los tokens de recuperación y revisión son valores aleatorios opacos. Solo su hash SHA-256 se guarda en `dbo.Token`.
- `Type` separa los propósitos `password_reset` y `proposal_review`, por lo que un token no puede utilizarse en el otro flujo.
- Los tokens expiran, pueden revocarse y solo pueden consumirse una vez.
- El token de revisión relaciona internamente `UserId` y `PropuestaId`; esos identificadores no se incluyen en la URL.

Los tiempos se configuran mediante `ActionTokens:PasswordResetMinutes` y `ActionTokens:ProposalReviewMinutes`. El cambio requerido en la base de datos está documentado en `Database/20260813_action_tokens_and_proposal_status.sql`.

En el modelo actual, `Token.UserId` identifica al representante autenticado que creó la propuesta y referencia `UserRepresentante.RepresentanteId`. El cliente demuestra autorización mediante la posesión del enlace enviado a su correo. Si posteriormente los clientes tienen una cuenta autenticada propia, este campo debe relacionarse con el identificador real del cliente y el backend debe comparar esa identidad; esa comparación no debe delegarse al frontend.

La página de revisión extrae el token de la URL y lo envía en el cuerpo de `POST /api/PropuestaCliente/revision/validar`. Para responder, envía el mismo token y `Aceptado` o `Cancelado` a `POST /api/PropuestaCliente/revision`. El backend cambia `propuesta.Estado` y consume el token en una sola transacción.

## Verificación

```powershell
dotnet restore OrdenesOnline-API/OrdenesOnline-API.slnx
dotnet build OrdenesOnline-API/OrdenesOnline-API.slnx --no-restore
dotnet test OrdenesOnline.Tests/OrdenesOnline.Tests.csproj --no-restore
dotnet list OrdenesOnline-API/OrdenesOnline-API.slnx package --vulnerable --include-transitive
```
