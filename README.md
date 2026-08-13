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
- `GET /api/Valor`

Los errores utilizan `ProblemDetails`. Login inválido devuelve `401`, validación incorrecta devuelve `400`, autorización insuficiente devuelve `403` y la creación de una propuesta devuelve `201`.

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
Email__From
Email__User
Email__Pass
```

Las credenciales que estuvieron versionadas deben rotarse aunque ya no aparezcan en los archivos actuales, porque siguen existiendo en el historial de Git.

## Tokens

- Los access tokens tienen audiencia `ClientesFrontend`, propósito `access` y duración configurable.
- Los tokens de recuperación tienen audiencia `ClientesPasswordReset`, propósito `password_reset` y una duración menor.
- Un token de recuperación no puede autenticar llamadas protegidas y un access token no puede cambiar una contraseña.

La recuperación continúa siendo stateless: hasta incorporar persistencia, un token de recuperación válido puede reutilizarse durante su vigencia.

## Verificación

```powershell
dotnet restore OrdenesOnline-API/OrdenesOnline-API.slnx
dotnet build OrdenesOnline-API/OrdenesOnline-API.slnx --no-restore
dotnet test OrdenesOnline.Tests/OrdenesOnline.Tests.csproj --no-restore
dotnet list OrdenesOnline-API/OrdenesOnline-API.slnx package --vulnerable --include-transitive
```
