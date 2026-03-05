using OrdenesOnline.Domain.DTO;
using OrdenesOnline.Domain.entities;
using OrdenesOnline.Domain.interfaces;

namespace OrdenesOnline.Application.Services
{
    public class RepresentanteService
    {
        private readonly IRepresentanteRepository _repo;
        private readonly TokenService _tokenService;

        public RepresentanteService(IRepresentanteRepository repo, TokenService tokenService)
        {
            _repo = repo;
            _tokenService = tokenService;
        }

        public Task<IEnumerable<Representante>> GetAll() => _repo.GetAllAsync();
        public Task<Representante?> GetById(int id) => _repo.GetByIdAsync(id);

        public Task<Representante?> GetByEmail(string email) => _repo.GetByEmail(email);
        public Task Add(Representante c) => _repo.AddAsync(c);
        public Task Update(Representante c) => _repo.UpdateAsync(c);
        public Task Delete(int id) => _repo.DeleteAsync(id);
        public Task<PasswordValidationResult?> Login(string correoCorporativo, string password) => _repo.Login(correoCorporativo,password);
        public Task<bool> UpdatePassword(string correoCorporativo, string password) => _repo.UpdatePassword(correoCorporativo, password);

        public async Task<bool> UpdatePasswordByToken(string token, string password)
        {
            var correo = _tokenService.ValidateToken(token);

            if (string.IsNullOrWhiteSpace(correo))
                return false;

            return await _repo.UpdatePassword(correo, password);
        }
    }
}
