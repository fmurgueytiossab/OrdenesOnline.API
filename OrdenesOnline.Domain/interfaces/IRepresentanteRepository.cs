using OrdenesOnline.Domain.DTO;
using OrdenesOnline.Domain.entities;
namespace OrdenesOnline.Domain.interfaces
{
    public interface IRepresentanteRepository
    {
        Task<IEnumerable<Representante>> GetAllAsync();
        Task<RepresentanteDTO?> GetByIdAsync(int id);
        Task AddAsync(Representante representante);
        Task UpdateAsync(Representante representante);
        Task DeleteAsync(int id);
        Task<PasswordValidationResult?> Login(string correoCorporativo, string password);
        Task<bool> UpdatePassword(string correoCorporativo, string password);
        Task<Representante?> GetByEmail(string email);
    }
}
