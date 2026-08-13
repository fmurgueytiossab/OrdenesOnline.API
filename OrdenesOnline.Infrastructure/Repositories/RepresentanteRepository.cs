using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using OrdenesOnline.Domain.DTO;
using OrdenesOnline.Domain.entities;
using OrdenesOnline.Domain.interfaces;
using OrdenesOnline.Infrastructure.Persistence;

namespace OrdenesOnline.Infrastructure.Repositories;

public sealed class RepresentanteRepository : IRepresentanteRepository
{
    private readonly AppDbContext _context;

    public RepresentanteRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<Representante>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await _context.Representantes.AsNoTracking().ToListAsync(cancellationToken);

    public async Task AddAsync(Representante representante, CancellationToken cancellationToken = default)
    {
        await _context.Representantes.AddAsync(representante, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Representante representante, CancellationToken cancellationToken = default)
    {
        _context.Representantes.Update(representante);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var entity = await _context.Representantes.FindAsync([id], cancellationToken);
        if (entity is null)
        {
            return;
        }

        _context.Representantes.Remove(entity);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<RepresentanteDTO?> GetByIdAsync(
        int representanteId,
        CancellationToken cancellationToken = default)
    {
        var user = await _context.Representantes
            .AsNoTracking()
            .Where(user => user.RepresentanteId == representanteId)
            .Select(user => new RepresentanteDTO
            {
                RepresentanteId = user.RepresentanteId,
                Nombre = user.Nombre,
                CorreoCorporativo = user.CorreoCorporativo,
                Dni = user.Dni
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (user is null)
        {
            return null;
        }

        user.Cosabcli = await _context.CodeRepresentantes
            .AsNoTracking()
            .Where(code => code.RepresentanteId == representanteId)
            .Select(code => code.Cosabcli)
            .ToListAsync(cancellationToken);

        return user;
    }

    public async Task<PasswordValidationResult?> Login(
        string correo,
        string password,
        CancellationToken cancellationToken = default)
    {
        var results = await _context.Set<PasswordValidationResult>()
            .FromSqlRaw(
                "EXEC usp_isValidRepresentantePassword @correo, @password",
                new SqlParameter("@correo", correo),
                new SqlParameter("@password", password))
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return results.FirstOrDefault();
    }

    public async Task<bool> UpdatePassword(
        string correo,
        string password,
        CancellationToken cancellationToken = default)
    {
        var rowsAffected = await _context.Database.ExecuteSqlRawAsync(
            "EXEC usp_updateRepresentantePassword @correo, @password",
            [new SqlParameter("@correo", correo), new SqlParameter("@password", password)],
            cancellationToken);

        return rowsAffected > 0;
    }

    public Task<Representante?> GetByEmail(string email, CancellationToken cancellationToken = default) =>
        _context.Representantes
            .AsNoTracking()
            .FirstOrDefaultAsync(user => user.CorreoCorporativo == email, cancellationToken);
}
