namespace OrdenesOnline.Domain.DTO;

public sealed record ClienteSearchResult(
    string Cosabcli,
    string NombreCompleto,
    IReadOnlyList<string> Emails,
    IReadOnlyList<string> Nucel,
    string? BloqueoMotivo)
{
    public static ClienteSearchResult Create(
        string cosabcli,
        string? names,
        string? paternalSurname,
        string? maternalSurname,
        string? clientDescription,
        string? emails,
        string? mobileNumber,
        bool isJointAccount,
        IEnumerable<string?>? authorizedRepresentativeMobileNumbers = null,
        string? blockReason = null) =>
        new(
            cosabcli,
            isJointAccount
                ? clientDescription?.Trim() ?? string.Empty
                : BuildFullName(names, paternalSurname, maternalSurname),
            ParseEmails(emails),
            isJointAccount
                ? ParseMobileNumbers(authorizedRepresentativeMobileNumbers ?? [])
                : ParseMobileNumbers([mobileNumber]),
            string.IsNullOrWhiteSpace(blockReason) ? null : blockReason.Trim());

    private static string BuildFullName(
        string? names,
        string? paternalSurname,
        string? maternalSurname) =>
        string.Join(
            ' ',
            new[] { names, paternalSurname, maternalSurname }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!.Trim()));

    private static IReadOnlyList<string> ParseEmails(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

    private static IReadOnlyList<string> ParseMobileNumbers(IEnumerable<string?> values) =>
        values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .SelectMany(value => value!.Split(
                ';',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
