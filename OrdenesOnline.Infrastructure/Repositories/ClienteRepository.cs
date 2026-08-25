using Microsoft.EntityFrameworkCore;
using OrdenesOnline.Domain.DTO;
using OrdenesOnline.Domain.entities;
using OrdenesOnline.Domain.interfaces;
using OrdenesOnline.Infrastructure.Persistence;

namespace OrdenesOnline.Infrastructure.Repositories;

public sealed class ClienteRepository : IClienteRepository
{
    private const string LikeEscapeCharacter = "~";
    private readonly OpersabDbContext _context;

    public ClienteRepository(OpersabDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<ClienteSearchResult>> SearchAsync(
        string search,
        int take,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Clientes
            .AsNoTracking()
            .Where(cliente => cliente.Estado != "9");

        var terms = search.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var term in terms)
        {
            var pattern = $"%{EscapeLikePattern(term)}%";
            query = query.Where(cliente =>
                EF.Functions.Like(cliente.Nombres ?? string.Empty, pattern, LikeEscapeCharacter) ||
                EF.Functions.Like(cliente.Apepat ?? string.Empty, pattern, LikeEscapeCharacter) ||
                EF.Functions.Like(cliente.Apemat ?? string.Empty, pattern, LikeEscapeCharacter) ||
                EF.Functions.Like(cliente.Descli ?? string.Empty, pattern, LikeEscapeCharacter));
        }

        var clients = await query
            .OrderBy(cliente => cliente.Apepat ?? cliente.Descli)
            .ThenBy(cliente => cliente.Apemat)
            .ThenBy(cliente => cliente.Nombres)
            .Select(cliente => new
            {
                cliente.Cosabcli,
                cliente.Nombres,
                cliente.Apepat,
                cliente.Apemat,
                cliente.Emailcli,
                cliente.Nucel,
                cliente.FgMancomunado,
                cliente.Descli
            })
            .Take(take)
            .ToListAsync(cancellationToken);

        var jointClientCodes = clients
            .Where(cliente => Cliente.IsJointAccount(cliente.FgMancomunado))
            .Select(cliente => cliente.Cosabcli)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var representativeMobileNumbers = new Dictionary<string, IReadOnlyList<string?>>(
            StringComparer.OrdinalIgnoreCase);

        if (jointClientCodes.Count > 0)
        {
            var authorizedRepresentatives = await _context.ClientesApoderados
                .AsNoTracking()
                .Where(representative => jointClientCodes.Contains(representative.Cosabcli))
                .Select(representative => new
                {
                    representative.Cosabcli,
                    representative.Nucel
                })
                .ToListAsync(cancellationToken);

            representativeMobileNumbers = authorizedRepresentatives
                .GroupBy(representative => representative.Cosabcli, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyList<string?>)group.Select(item => item.Nucel).ToList(),
                    StringComparer.OrdinalIgnoreCase);
        }

        var clientCodes = clients
            .Select(cliente => cliente.Cosabcli)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var blockReasons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (clientCodes.Count > 0)
        {
            var blocks = await _context.ClientesBloqueos
                .AsNoTracking()
                .Where(block => clientCodes.Contains(block.Cosabcli))
                .Select(block => new
                {
                    block.Cosabcli,
                    block.Glosa
                })
                .ToListAsync(cancellationToken);

            blockReasons = blocks
                .GroupBy(block => block.Cosabcli, StringComparer.OrdinalIgnoreCase)
                .Select(group => new
                {
                    group.Key,
                    Reason = string.Join(
                        "; ",
                        group.Select(block => block.Glosa?.Trim())
                            .Where(glosa => !string.IsNullOrWhiteSpace(glosa))
                            .Distinct(StringComparer.OrdinalIgnoreCase))
                })
                .Where(block => block.Reason.Length > 0)
                .ToDictionary(
                    block => block.Key,
                    block => block.Reason,
                    StringComparer.OrdinalIgnoreCase);
        }

        return clients
            .Select(cliente =>
            {
                var isJointAccount = Cliente.IsJointAccount(cliente.FgMancomunado);
                representativeMobileNumbers.TryGetValue(
                    cliente.Cosabcli,
                    out var authorizedMobileNumbers);
                blockReasons.TryGetValue(cliente.Cosabcli, out var blockReason);

                return ClienteSearchResult.Create(
                    cliente.Cosabcli,
                    cliente.Nombres,
                    cliente.Apepat,
                    cliente.Apemat,
                    cliente.Descli,
                    cliente.Emailcli,
                    cliente.Nucel,
                    isJointAccount,
                    authorizedMobileNumbers,
                    blockReason);
            })
            .ToList();
    }

    private static string EscapeLikePattern(string value) => value
        .Replace(LikeEscapeCharacter, LikeEscapeCharacter + LikeEscapeCharacter)
        .Replace("%", LikeEscapeCharacter + "%")
        .Replace("_", LikeEscapeCharacter + "_")
        .Replace("[", LikeEscapeCharacter + "[");
}
