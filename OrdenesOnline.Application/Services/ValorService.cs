using OrdenesOnline.Domain.entities;
using OrdenesOnline.Domain.interfaces;

namespace OrdenesOnline.Application.Services;

public sealed class ValorService
{
    private readonly IValorRepository _repository;

    public ValorService(IValorRepository repository)
    {
        _repository = repository;
    }

    public Task<IEnumerable<Valor>> GetAll(CancellationToken cancellationToken = default) =>
        _repository.GetAllAsync(cancellationToken);
}
