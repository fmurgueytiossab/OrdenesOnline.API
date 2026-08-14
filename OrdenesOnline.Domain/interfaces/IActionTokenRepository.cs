using OrdenesOnline.Domain.entities;

namespace OrdenesOnline.Domain.interfaces;

public interface IActionTokenRepository
{
    Task AddAsync(ActionToken token, CancellationToken cancellationToken = default);

    Task<ActionToken?> GetActiveByHashAsync(
        string tokenHash,
        string type,
        DateTime now,
        CancellationToken cancellationToken = default);

    Task RevokeActiveAsync(
        int userId,
        string type,
        int? propuestaId,
        CancellationToken cancellationToken = default);

    Task<bool> TryMarkUsedAsync(
        int tokenId,
        DateTime now,
        CancellationToken cancellationToken = default);

    Task<bool> TryApplyProposalDecisionAsync(
        int tokenId,
        int propuestaId,
        string estado,
        DateTime now,
        CancellationToken cancellationToken = default);
}
