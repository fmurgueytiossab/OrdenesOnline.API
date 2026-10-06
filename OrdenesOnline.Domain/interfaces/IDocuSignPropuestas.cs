using OrdenesOnline.Domain.DTO;
using OrdenesOnline.Domain.entities;

namespace OrdenesOnline.Domain.interfaces;

public sealed record DocuSignContacto(string Funcion, string Nombre, string Celular, string? Identificacion = null);
public sealed record DocuSignContactos(IReadOnlyList<DocuSignContacto> Firmantes, string? Error = null);
public sealed record DocuSignPreparacion(Guid TemplateId, IReadOnlyList<DocuSignContacto> Firmantes);
public sealed record DocuSignEnvio(Guid? EnvelopeId, bool Enviado, string? Advertencia = null);
public sealed record DocuSignFirmante(string RecipientId, string Funcion, string Nombre, string Celular,
    string Estado, DateTime? FechaFirmaUtc = null, string? Identificacion = null);
public sealed record DocuSignSobre(long Id, int PropuestaId, Guid EnvelopeId, Guid AccountId,
    string Entorno, string Estado, int FirmasRequeridas, IReadOnlyList<DocuSignFirmante> Firmantes);
public sealed record DocuSignEstado(string Estado, DateTime FechaEstadoUtc, DateTime? CompletadoUtc,
    IReadOnlyList<DocuSignFirmante> Firmantes, DateTime? FechaEnvioUtc = null);
public sealed record DocuSignArchivo(string Ruta, string Sha256);

public interface IClienteFirmantesRepository
{
    Task<DocuSignContactos> GetAsync(string cosabcli, CancellationToken ct);
}

public interface IDocuSignPropuestas
{
    Task<DocuSignPreparacion> PrepareAsync(string cosabcli, CancellationToken ct);
    Task<DocuSignEnvio> SendAsync(Propuesta propuesta, PropuestaClienteCreateRequest request,
        DocuSignPreparacion preparacion, CancellationToken ct);
}

public interface IPropuestaDocuSignRepository
{
    Task<long> RegisterAsync(int propuestaId, Guid envelopeId, Guid accountId, string entorno,
        Guid templateId, IReadOnlyList<DocuSignFirmante> firmantes, CancellationToken ct);
    Task<bool> HasAsync(int propuestaId, CancellationToken ct);
    Task<DocuSignSobre?> FindAsync(Guid envelopeId, Guid accountId, string entorno, CancellationToken ct);
    Task ApplyAsync(DocuSignSobre sobre, DocuSignEstado estado, CancellationToken ct);
    Task<IReadOnlyList<DocuSignSobre>> PendingDownloadsAsync(Guid accountId, string entorno, CancellationToken ct);
    Task ArchiveAsync(long id, DocuSignArchivo? archivo, string? error, CancellationToken ct);
}
