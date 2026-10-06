using Microsoft.Extensions.Logging;
using OrdenesOnline.Domain.DTO;
using OrdenesOnline.Domain.entities;
using OrdenesOnline.Domain.interfaces;

namespace OrdenesOnline.Application.Services;

public sealed class DocuSignContactException(string message) : Exception(message);

public sealed class DocuSignPropuestasService(IClienteFirmantesRepository contacts, IPropuestaDocuSignRepository repository,
    DocuSignClient client, DocuSignOptions options, ILogger<DocuSignPropuestasService> logger) : IDocuSignPropuestas
{
    public async Task<DocuSignPreparacion> PrepareAsync(string cosabcli, CancellationToken ct)
    {
        var result = await contacts.GetAsync(cosabcli.Trim(), ct);
        if (result.Error is not null) throw new DocuSignContactException(result.Error);
        var plan = new DocuSignPreparacion(result.Firmantes.Count == 1 ? options.IndividualTemplateId : options.JointTemplateId, result.Firmantes);
        await client.TemplateSigners(plan, ct);
        return plan;
    }

    public async Task<DocuSignEnvio> SendAsync(Propuesta propuesta, PropuestaClienteCreateRequest request, DocuSignPreparacion plan, CancellationToken ct)
    {
        Guid? envelope = null;
        bool registered = false;
        try
        {
            var signers = await client.TemplateSigners(plan, ct);
            envelope = await client.CreateDraft(propuesta, request, plan, signers, ct);
            await repository.RegisterAsync(propuesta.PropuestaId, envelope.Value, options.AccountId, options.Entorno, plan.TemplateId, signers, ct);
            registered = true;
            await client.Send(envelope.Value, ct);
            // Connect confirma estados. No escribir 'sent' despues de un webhook 'completed'.
            return new(envelope, true);
        }
        catch (Exception ex)
        {
            // No incluir claves, JWT, respuestas HTTP ni telefonos en registros/advertencias.
            logger.LogWarning("Envio DocuSign no confirmado: propuesta {PropuestaId}, sobre {EnvelopeId}, registrado {Registered}, tipo {ErrorType}.",
                propuesta.PropuestaId, envelope, registered, ex.GetType().Name);
            return new(envelope, false, registered
                ? "El sobre esta registrado, pero el envio no pudo confirmarse. Revise Acuerdos antes de repetir; no cree otra propuesta."
                : "La propuesta quedo pendiente y no se envio para firma. Revise la configuracion y el registro del servidor.");
        }
    }
}
