using Microsoft.EntityFrameworkCore;
using OrdenesOnline.Domain.entities;
using OrdenesOnline.Domain.interfaces;
using OrdenesOnline.Infrastructure.Persistence;

namespace OrdenesOnline.Infrastructure.Repositories;

public sealed class ActionTokenRepository : IActionTokenRepository
{
    private readonly AppDbContext _context;

    public ActionTokenRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(ActionToken token, CancellationToken cancellationToken = default)
    {
        await _context.ActionTokens.AddAsync(token, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public Task<ActionToken?> GetActiveByHashAsync(
        string tokenHash,
        string type,
        DateTime now,
        CancellationToken cancellationToken = default) =>
        _context.ActionTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(
                token => token.TokenHash == tokenHash &&
                         token.Type == type &&
                         !token.IsUsed &&
                         !token.IsRevoked &&
                         token.ExpiresAt > now,
                cancellationToken);

    public async Task RevokeActiveAsync(
        int userId,
        string type,
        int? propuestaId,
        CancellationToken cancellationToken = default)
    {
        await _context.ActionTokens
            .Where(token => token.UserId == userId &&
                            token.Type == type &&
                            token.PropuestaId == propuestaId &&
                            !token.IsUsed &&
                            !token.IsRevoked)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(token => token.IsRevoked, true),
                cancellationToken);
    }

    public async Task<bool> TryMarkUsedAsync(
        int tokenId,
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        var updatedRows = await _context.ActionTokens
            .Where(token => token.TokenId == tokenId &&
                            !token.IsUsed &&
                            !token.IsRevoked &&
                            token.ExpiresAt > now)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(token => token.IsUsed, true),
                cancellationToken);

        return updatedRows == 1;
    }

    public async Task<bool> TryApplyProposalDecisionAsync(
        int tokenId,
        int propuestaId,
        string estado,
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        var updatedTokens = await _context.ActionTokens
            .Where(token => token.TokenId == tokenId &&
                            token.PropuestaId == propuestaId &&
                            token.Type == ActionTokenTypes.ProposalReview &&
                            !token.IsUsed &&
                            !token.IsRevoked &&
                            token.ExpiresAt > now)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(token => token.IsUsed, true),
                cancellationToken);

        if (updatedTokens != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        var updatedProposals = await _context.Propuestas
            .Where(propuesta => propuesta.PropuestaId == propuestaId &&
                                propuesta.Estado == PropuestaEstados.Pendiente)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(propuesta => propuesta.Estado, estado),
                cancellationToken);

        if (updatedProposals != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }
}
