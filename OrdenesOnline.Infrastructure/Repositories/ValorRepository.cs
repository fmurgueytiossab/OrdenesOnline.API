using Microsoft.EntityFrameworkCore;
using OrdenesOnline.Domain.entities;
using OrdenesOnline.Domain.interfaces;
using OrdenesOnline.Infrastructure.Persistence;

namespace OrdenesOnline.Infrastructure.Repositories;

public sealed class ValorRepository : IValorRepository
{
    private readonly OpersabDbContext _context;

    public ValorRepository(OpersabDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<Valor>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await _context.Valores
            .AsNoTracking()
            .Where(valor => valor.Estado != "9")
            .Select(valor => new Valor
            {
                Cosabval = valor.Cosabval,
                Mnemo = valor.Mnemo,
                Comon = valor.Comon,
                Tival = valor.Tival
            })
            .ToListAsync(cancellationToken);
}
