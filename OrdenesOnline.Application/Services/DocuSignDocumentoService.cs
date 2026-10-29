using OrdenesOnline.Domain.interfaces;

namespace OrdenesOnline.Application.Services;

public sealed class DocuSignDocumentoService(IPropuestaRepository propuestas,
    IRepresentanteClientScopeRepository clientScope, IPropuestaDocuSignRepository sobres,
    DocuSignClient client, DocuSignOptions options)
{
    public async Task<DocuSignDocumentoResult> Download(int representanteId, int propuestaId, CancellationToken ct)
    {
        var scope = await clientScope.GetAsync(representanteId, ct);
        if (!scope.RepresentanteExiste) return new(DocuSignDocumentoStatus.RepresentanteNotFound);
        var propuesta = await propuestas.GetByIdAsync(propuestaId, ct);
        if (propuesta is null || !scope.Cosabcli.Contains(propuesta.Cosabcli.Trim(), StringComparer.OrdinalIgnoreCase))
            return new(DocuSignDocumentoStatus.NotFound);

        // La cuenta y el sobre se resuelven en el servidor; no se aceptan IDs de DocuSign del navegador.
        var sobre = await sobres.FindByProposalAsync(propuestaId, options.AccountId, ct);
        if (sobre is null) return new(DocuSignDocumentoStatus.NotFound);
        if (sobre.Estado != "completed" || sobre.FirmasRequeridas is not (1 or 2 or 3)
            || sobre.Firmantes.Count != sobre.FirmasRequeridas
            || sobre.Firmantes.Any(s => s.Estado != "completed"))
            return new(DocuSignDocumentoStatus.NotCompleted);

        var state = await client.Status(sobre, ct);
        if (state.Estado != "completed") return new(DocuSignDocumentoStatus.NotCompleted);

        // Solo memoria durante esta solicitud. No se guardan PDF, metadatos ni estados de descarga.
        var pdf = await client.DownloadCombinedPdf(sobre.EnvelopeId, ct);
        return new(DocuSignDocumentoStatus.Success, pdf);
    }
}

public enum DocuSignDocumentoStatus { Success, RepresentanteNotFound, NotFound, NotCompleted }
public sealed record DocuSignDocumentoResult(DocuSignDocumentoStatus Status, byte[]? Pdf = null);
