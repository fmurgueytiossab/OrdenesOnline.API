using OrdenesOnline.Domain.DTO;
using OrdenesOnline.Domain.entities;
using OrdenesOnline.Domain.interfaces;

namespace OrdenesOnline.Application.Services;

public sealed class RepresentanteService
{
    private readonly IRepresentanteRepository _repository;
    private readonly ActionTokenService _actionTokenService;

    public RepresentanteService(
        IRepresentanteRepository repository,
        ActionTokenService actionTokenService)
    {
        _repository = repository;
        _actionTokenService = actionTokenService;
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
        var storedToken = await _actionTokenService.ValidateAsync(
            token,
            ActionTokenService.PasswordResetType,
            cancellationToken);

        if (storedToken is null ||
            !await _actionTokenService.TryMarkUsedAsync(storedToken.TokenId, cancellationToken))
        {
            return false;
        }

        var representante = await _repository.GetByIdAsync(storedToken.UserId, cancellationToken);
        if (representante is null)
        {
            return false;
        }

        return await _repository.UpdatePassword(
            representante.CorreoCorporativo,
            password,
            cancellationToken);
    }
}
