using OrdenesOnline.Domain.entities;
using OrdenesOnline.Domain.interfaces;

namespace OrdenesOnline.Tests.TestDoubles;

internal sealed class FakeActionTokenRepository : IActionTokenRepository
{
    private int _nextId = 1;

    public List<ActionToken> Tokens { get; } = [];

    public Task AddAsync(ActionToken token, CancellationToken cancellationToken = default)
    {
        token.TokenId = _nextId++;
        Tokens.Add(token);
        return Task.CompletedTask;
    }

    public Task<ActionToken?> GetActiveByHashAsync(
        string tokenHash,
        string type,
        DateTime now,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Tokens.FirstOrDefault(token =>
            token.TokenHash == tokenHash &&
            token.Type == type &&
            !token.IsUsed &&
            !token.IsRevoked &&
            token.ExpiresAt > now));

    public Task RevokeActiveAsync(
        int userId,
        string type,
        int? propuestaId,
        CancellationToken cancellationToken = default)
    {
        foreach (var token in Tokens.Where(token =>
                     token.UserId == userId &&
                     token.Type == type &&
                     token.PropuestaId == propuestaId &&
                     !token.IsUsed &&
                     !token.IsRevoked))
        {
            token.IsRevoked = true;
        }

        return Task.CompletedTask;
    }

    public Task<bool> TryMarkUsedAsync(
        int tokenId,
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        var token = Tokens.FirstOrDefault(token =>
            token.TokenId == tokenId &&
            !token.IsUsed &&
            !token.IsRevoked &&
            token.ExpiresAt > now);

        if (token is null)
        {
            return Task.FromResult(false);
        }

        token.IsUsed = true;
        return Task.FromResult(true);
    }

    public Task<bool> TryApplyProposalDecisionAsync(
        int tokenId,
        int propuestaId,
        string estado,
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        var token = Tokens.FirstOrDefault(token =>
            token.TokenId == tokenId &&
            token.PropuestaId == propuestaId &&
            token.Type == ActionTokenTypes.ProposalReview &&
            !token.IsUsed &&
            !token.IsRevoked &&
            token.ExpiresAt > now);

        if (token is null)
        {
            return Task.FromResult(false);
        }

        token.IsUsed = true;
        return Task.FromResult(true);
    }
}
