using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using OrdenesOnline.Domain.entities;
using OrdenesOnline.Domain.interfaces;

namespace OrdenesOnline.Application.Services;

public sealed class ActionTokenService
{
    public const string PasswordResetType = ActionTokenTypes.PasswordReset;
    public const string ProposalReviewType = ActionTokenTypes.ProposalReview;

    private readonly IActionTokenRepository _repository;
    private readonly int _passwordResetMinutes;
    private readonly int _proposalReviewMinutes;

    public ActionTokenService(IActionTokenRepository repository, IConfiguration configuration)
    {
        _repository = repository;
        _passwordResetMinutes = GetPositiveInt(configuration, "ActionTokens:PasswordResetMinutes", 15);
        _proposalReviewMinutes = GetPositiveInt(configuration, "ActionTokens:ProposalReviewMinutes", 1440);
    }

    public Task<IssuedActionToken> CreatePasswordResetTokenAsync(
        int userId,
        CancellationToken cancellationToken = default) =>
        CreateAsync(userId, null, PasswordResetType, _passwordResetMinutes, cancellationToken);

    public Task<IssuedActionToken> CreateProposalReviewTokenAsync(
        int userId,
        int propuestaId,
        CancellationToken cancellationToken = default) =>
        CreateAsync(userId, propuestaId, ProposalReviewType, _proposalReviewMinutes, cancellationToken);

    public Task<ActionToken?> ValidateAsync(
        string rawToken,
        string type,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return Task.FromResult<ActionToken?>(null);
        }

        return _repository.GetActiveByHashAsync(
            Hash(rawToken),
            type,
            DateTime.UtcNow,
            cancellationToken);
    }

    public Task<bool> TryMarkUsedAsync(
        int tokenId,
        CancellationToken cancellationToken = default) =>
        _repository.TryMarkUsedAsync(tokenId, DateTime.UtcNow, cancellationToken);

    public Task<bool> TryApplyProposalDecisionAsync(
        int tokenId,
        int propuestaId,
        string estado,
        CancellationToken cancellationToken = default) =>
        _repository.TryApplyProposalDecisionAsync(
            tokenId,
            propuestaId,
            estado,
            DateTime.UtcNow,
            cancellationToken);

    private async Task<IssuedActionToken> CreateAsync(
        int userId,
        int? propuestaId,
        string type,
        int lifetimeMinutes,
        CancellationToken cancellationToken)
    {
        await _repository.RevokeActiveAsync(
            userId,
            type,
            propuestaId,
            cancellationToken);

        var rawToken = Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var now = DateTime.UtcNow;
        var token = new ActionToken
        {
            UserId = userId,
            PropuestaId = propuestaId,
            TokenHash = Hash(rawToken),
            Type = type,
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(lifetimeMinutes),
            IsUsed = false,
            IsRevoked = false
        };

        await _repository.AddAsync(token, cancellationToken);
        return new IssuedActionToken(rawToken, token.TokenId, token.ExpiresAt);
    }

    private static string Hash(string rawToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

    private static string Base64UrlEncode(byte[] value) =>
        Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static int GetPositiveInt(IConfiguration configuration, string key, int fallback) =>
        int.TryParse(configuration[key], out var value) && value > 0 ? value : fallback;
}

public sealed record IssuedActionToken(string Value, int TokenId, DateTime ExpiresAt);
