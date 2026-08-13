using OrdenesOnline.Domain.DTO;
using OrdenesOnline.Domain.entities;

namespace OrdenesOnline.Domain.interfaces;

public interface IRepresentanteRepository
{
    Task<IEnumerable<Representante>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<RepresentanteDTO?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task AddAsync(Representante representante, CancellationToken cancellationToken = default);
    Task UpdateAsync(Representante representante, CancellationToken cancellationToken = default);
    Task DeleteAsync(int id, CancellationToken cancellationToken = default);
    Task<PasswordValidationResult?> Login(
        string correoCorporativo,
        string password,
        CancellationToken cancellationToken = default);
    Task<bool> UpdatePassword(
        string correoCorporativo,
        string password,
        CancellationToken cancellationToken = default);
    Task<Representante?> GetByEmail(string email, CancellationToken cancellationToken = default);
}
