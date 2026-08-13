using OrdenesOnline.Domain.DTO;
using OrdenesOnline.Domain.entities;
using OrdenesOnline.Domain.interfaces;

namespace OrdenesOnline.Application.Services;

public sealed class RepresentanteService
{
    private readonly IRepresentanteRepository _repository;
    private readonly TokenService _tokenService;

    public RepresentanteService(IRepresentanteRepository repository, TokenService tokenService)
    {
        _repository = repository;
        _tokenService = tokenService;
    }

    public Task<IEnumerable<Representante>> GetAll(CancellationToken cancellationToken = default) =>
        _repository.GetAllAsync(cancellationToken);

    public Task<RepresentanteDTO?> GetById(int id, CancellationToken cancellationToken = default) =>
        _repository.GetByIdAsync(id, cancellationToken);

    public Task<Representante?> GetByEmail(string email, CancellationToken cancellationToken = default) =>
        _repository.GetByEmail(email, cancellationToken);

    public Task<PasswordValidationResult?> Login(
        string correoCorporativo,
        string password,
        CancellationToken cancellationToken = default) =>
        _repository.Login(correoCorporativo, password, cancellationToken);

    public async Task<bool> UpdatePasswordByToken(
        string token,
        string password,
        CancellationToken cancellationToken = default)
    {
        var correo = _tokenService.ValidatePasswordResetToken(token);

        if (string.IsNullOrWhiteSpace(correo))
        {
            return false;
        }

        return await _repository.UpdatePassword(correo, password, cancellationToken);
    }
}
