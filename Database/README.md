# Base de datos de OrdenesOnline.API

## Instalacion inicial

1. Seleccionar una base SQL Server vacia para ConnectionStrings:DefaultConnection.
2. Ejecutar `Create OrdenesOnline.Api.sql`.
3. Ejecutar `Seed OrdenesOnline.Api.sql` para cargar el dataset inicial solicitado.

Create conserva tipos, longitudes y nulabilidad de las tablas suministradas,
agrega Token requerido por el API e integra las dos tablas actuales de DocuSign.
Crea todo en una transaccion y rechaza objetos existentes. No crea una base fisica
ni cambia DefaultConnection. No usarlo como actualizacion de una base con datos.

Seed conserva RepresentanteId=1 y RepresentanteId=33 mediante IDENTITY_INSERT.
Asocia 1 con 026775 y 33 con 006186. Puede repetirse; no reemplaza usuarios ni
contrasenas existentes y falla ante conflictos de ID/correo.

## Inventario utilizado por DefaultConnection

| Objeto | Uso en el API |
| --- | --- |
| Propuesta | Creacion, consulta y estado de propuestas |
| UserRepresentante | Representantes, login y cambio de contrasena |
| CodeRepresentante | Codigos de referencia para resolver la cartera en Opersab |
| Token | Recuperacion de contrasena y revision por token; solo guarda el hash |
| PropuestaDocuSign | Sobre vigente, firmas requeridas y archivo firmado |
| PropuestaDocuSignFirmante | Destinatarios reales del sobre y estados de firma |
| usp_EncryptText / usp_DecryptText | Cifrado y descifrado con DATAWEB_KEY y DATAWEB_CERTIFICATE |
| usp_isValidRepresentantePassword | Validacion de login; devuelve IsValid y RepresentanteId |
| usp_updateRepresentantePassword | Cambio de contrasena utilizado por el repositorio |
| usp_getRepresentantePassword | Procedimiento suministrado; no tiene consumidor localizado en el API actual |

Se incluyen seis tablas y cinco procedimientos. PasswordValidationResult es un
resultado sin clave de un procedimiento: no necesita una tabla propia.
TokenHash se guarda como CHAR(64), porque el servicio genera SHA-256 hexadecimal.
Type es VARCHAR(20) y CK_Token_Type permite solo proposal_review y password_reset.
Se conservan los defaults a cero, ambas FK, el indice unico de TokenHash y el
indice compuesto UserId/Type para sus consultas.

## Cifrado existente

Las definiciones recibidas abren DATAWEB_KEY mediante DATAWEB_CERTIFICATE.
Esos objetos criptograficos y su cadena de proteccion se provisionan/restauran
aparte; sus definiciones y respaldos no fueron suministrados. El Create incluye
los procedimientos reales, sin crear claves diferentes bajo el mismo nombre.
Los bytes del dataset solo sirven con la clave original: crear otra clave y otro
certificado con los mismos nombres no recupera esas contrasenas.
Por ello, validar login con el dataset requiere restaurar las claves originales
en el ambiente de destino. Con las claves disponibles, el cambio de contrasena
usa los procedimientos incluidos. Se conservan las firmas/longitudes recibidas:
correo VARCHAR(50), login password VARCHAR(100) y variable descifrada NVARCHAR(20).

## Dependencias de Opersab

ConnectionStrings:Opersab corresponde a otra base: Valores, clientes,
clientes_apoderados y clientes_bloqueos se consultan alli. No se recrean ni se
cargan desde este dataset. Los codigos 026775 y 006186 deben existir en ese ambiente
para que la resolucion de gestores/cartera encuentre clientes.

## Bases existentes

Create OrdenesOnline.Api.sql es la unica fuente del esquema de instalacion:
incluye las dos tablas de DocuSign y las restricciones para uno, dos o tres firmantes.
Los scripts SQL por fecha se retiraron porque esa estructura ya esta consolidada.
Para una base existente, comparar su esquema y aplicar solo los cambios necesarios;
el Create rechaza objetos existentes y no es un actualizador.

## DocuSign con tres titulares

El API selecciona la plantilla individual para un cliente individual, la plantilla
mancomunada actual para dos titulares y JointThreeTemplateId para tres titulares.
Todos reciben por WhatsApp su enlace al mismo sobre en paralelo.
La cuenta mancomunada requiere dos o tres filas en clientes_apoderados de Opersab,
con celulares validos y distintos una vez normalizados. Se conserva descli como
nombre compartido de la cuenta y el orden estable por celular de la integracion actual.

## Configuracion

Variable de entorno nueva, necesaria al enviar propuestas de tres titulares:

```text
DocuSign__JointThreeTemplateId=<GUID de la plantilla en la cuenta y ambiente configurados>
```

El tercer rol usa Titular3 por defecto. Se puede configurar mediante
DocuSign__JointThirdRole. Los dos primeros mantienen JointFirstRole y JointSecondRole.
En desarrollo, las claves equivalentes de User Secrets son DocuSign:JointThreeTemplateId
y DocuSign:JointThirdRole. Reiniciar el proceso/IIS despues de cambiar variables.
Sin la tercera plantilla los casos actuales de uno/dos firmantes siguen disponibles;
un caso de tres falla en la preparacion, antes de guardar la propuesta o crear un sobre.

No cambiar IntegrationKey, UserId, AccountId ni AuthServer para agregar una plantilla
de la misma cuenta y ambiente. Para Demo se conserva account-d.docusign.com.

## Plantilla

Tres destinatarios remotos, con roles Titular1, Titular2 y Titular3 (o los nombres
configurados), sin destinatarios adicionales. Cada uno tiene su boton nativo
AprobarTitularN y DeclinarTitularN, asignado a su destinatario y sin condiciones.
Para Titular3: AprobarTitular3 y DeclinarTitular3.

Mantener las etiquetas de texto de la plantilla de dos titulares:
TipoOperacion, Valor, Cantidad, Precio, Monto, Moneda, Vigencia, Mercado,
NombreRepresentante, NombreCuenta y CodigoCuenta. El API comprueba las etiquetas
y roles contra DocuSign antes de crear el sobre; no requiere IDs de destinatario fijos.

## Base instalada

Para una instalacion nueva usar Create OrdenesOnline.Api.sql, que ya incluye tres titulares.
En una base existente verificar las restricciones de FirmasRequeridas (1, 2, 3) y
Funcion (Cliente, Titular1, Titular2, Titular3) contra el Create antes de desplegar.
El API no modifica automaticamente el esquema ni recrea tablas al iniciar.

## Estados

Con una o dos firmas de tres, la propuesta sigue Enviado y no admite Ingresado.
Con las tres firmas confirmadas se muestra Firmado y se habilita Marcar ingresado.
El rechazo de cualquier titular termina en Cancelado; eventos posteriores no reabren
la propuesta. El documento se archiva solo con el sobre completed y todas las firmas.

## Verificacion local

Las pruebas SQL opt-in crean y eliminan solo bases temporales de LocalDB.
Comprueban el Create consolidado, repeticion del seed, IDs/asociaciones, tokens,
procedimientos con claves efimeras de prueba y el flujo DocuSign de uno/dos/tres.
No demuestran que los bytes cifrados del dataset sean portables sin las claves
originales. No se ejecutan estos scripts sobre el servidor de pruebas al compilar.
