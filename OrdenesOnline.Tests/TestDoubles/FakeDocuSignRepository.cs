using OrdenesOnline.Domain.interfaces;

namespace OrdenesOnline.Tests.TestDoubles;

public sealed class FakeDocuSignRepository : IPropuestaDocuSignRepository
{
    public bool Registered { get; private set; }
    public bool FailRegister { get; set; }
    public bool HasEnvelope { get; set; }
    public DocuSignSobre? Found { get; set; }
    public bool FailApply { get; set; }
    public int ApplyCalls { get; private set; }
    public DocuSignEstado? Applied { get; private set; }
    public IReadOnlyList<DocuSignFirmante> Signers { get; private set; } = [];
    public Task<long> RegisterAsync(int p, Guid e, Guid a, string env, Guid template, IReadOnlyList<DocuSignFirmante> signers, CancellationToken ct)
    {
        if (FailRegister) throw new InvalidOperationException("SQL no disponible");
        Registered = true; Signers = signers;
        return Task.FromResult(1L);
    }
    public Task<bool> HasAsync(int p, CancellationToken ct) => Task.FromResult(HasEnvelope);
    public Task<DocuSignSobre?> FindAsync(Guid e, Guid a, string env, CancellationToken ct) => Task.FromResult(Found);
    public Task ApplyAsync(DocuSignSobre s, DocuSignEstado e, CancellationToken ct)
    {
        if (FailApply) throw new InvalidOperationException("SQL no disponible");
        ApplyCalls++; Applied = e; return Task.CompletedTask;
    }
    public Task<IReadOnlyList<DocuSignSobre>> PendingDownloadsAsync(Guid a, string env, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<DocuSignSobre>>([]);
    public Task ArchiveAsync(long id, DocuSignArchivo? archive, string? error, CancellationToken ct) => Task.CompletedTask;
}
