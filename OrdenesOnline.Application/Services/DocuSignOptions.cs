using Microsoft.Extensions.Configuration;

namespace OrdenesOnline.Application.Services;

public sealed class DocuSignOptions
{
    public string IntegrationKey { get; }
    public string UserId { get; }
    public Guid AccountId { get; }
    public string AuthServer { get; }
    public string Entorno => AuthServer == "account-d.docusign.com" ? "Desarrollo" : "Produccion";
    public string PrivateKeyPath { get; }
    public Guid IndividualTemplateId { get; }
    public Guid JointTemplateId { get; }
    public Guid? JointThreeTemplateId { get; }
    public string IndividualRole { get; }
    public string JointFirstRole { get; }
    public string JointSecondRole { get; }
    public string JointThirdRole { get; }
    public string SignedDocumentsPath { get; }
    public string WebhookHmacSecret { get; }

    public DocuSignOptions(IConfiguration config)
    {
        string Required(string key) => !string.IsNullOrWhiteSpace(config[$"DocuSign:{key}"])
            ? config[$"DocuSign:{key}"]!.Trim()
            : throw new InvalidOperationException($"Falta DocuSign:{key}.");
        IntegrationKey = Guid.Parse(Required("IntegrationKey")).ToString();
        UserId = Guid.Parse(Required("UserId")).ToString();
        AccountId = Guid.Parse(Required("AccountId"));
        AuthServer = Required("AuthServer").ToLowerInvariant();
        if (AuthServer is not ("account-d.docusign.com" or "account.docusign.com"))
            throw new InvalidOperationException("DocuSign:AuthServer no es un servidor OAuth permitido.");
        PrivateKeyPath = Required("PrivateKeyPath");
        IndividualTemplateId = Guid.Parse(Required("IndividualTemplateId"));
        JointTemplateId = Guid.Parse(Required("JointTemplateId"));
        IndividualRole = Required("IndividualRole");
        JointFirstRole = Required("JointFirstRole");
        JointSecondRole = Required("JointSecondRole");
        // La tercera plantilla es opcional para conservar las instalaciones de uno/dos firmantes.
        var thirdTemplate = config["DocuSign:JointThreeTemplateId"]?.Trim();
        if (!string.IsNullOrEmpty(thirdTemplate))
        {
            if (!Guid.TryParse(thirdTemplate, out var templateId) || templateId == Guid.Empty)
                throw new InvalidOperationException("DocuSign:JointThreeTemplateId debe ser un GUID valido.");
            JointThreeTemplateId = templateId;
        }
        JointThirdRole = string.IsNullOrWhiteSpace(config["DocuSign:JointThirdRole"])
            ? "Titular3" : config["DocuSign:JointThirdRole"]!.Trim();
        if (JointThreeTemplateId is not null
            && new[] { JointFirstRole, JointSecondRole, JointThirdRole }.Distinct(StringComparer.Ordinal).Count() != 3)
            throw new InvalidOperationException("Las tres funciones de la plantilla mancomunada deben ser distintas.");
        SignedDocumentsPath = Required("SignedDocumentsPath");
        if (!Path.IsPathFullyQualified(SignedDocumentsPath))
            throw new InvalidOperationException("DocuSign:SignedDocumentsPath debe ser una ruta absoluta.");
        WebhookHmacSecret = Required("WebhookHmacSecret");
        // Connect a nivel de cuenta publica en esta URL; no se agrega otra notificacion por sobre.
        var webhook = Required("WebhookUrl");
        if (!Uri.TryCreate(webhook, UriKind.Absolute, out var uri) || uri.Scheme != "https")
            throw new InvalidOperationException("DocuSign:WebhookUrl debe ser HTTPS.");
    }
}
