using OrdenesOnline.Domain.DTO;
using OrdenesOnline.Domain.entities;
namespace OrdenesOnline.Domain.interfaces
{
    public interface IRepresentanteRepository
    {
        Task<IEnumerable<Representante>> GetAllAsync();
        Task<Representante?> GetByIdAsync(int id);
        Task AddAsync(Representante representante);
        Task UpdateAsync(Representante representante);
        Task DeleteAsync(int id);
        Task<PasswordValidationResult?> ValidatePassword(string correoCorporativo, string password);
        Task<bool> UpdatePassword(string correoCorporativo, string password);
        Task<Representante?> GetByEmail(string email);
    }
}
