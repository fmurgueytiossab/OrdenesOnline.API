using Microsoft.EntityFrameworkCore;
using OrdenesOnline.Domain.entities;
using OrdenesOnline.Domain.interfaces;
using OrdenesOnline.Infrastructure.Persistence;
using System.Text.RegularExpressions;

namespace OrdenesOnline.Infrastructure.Repositories;

public sealed class ClienteFirmantesRepository(OpersabDbContext context) : IClienteFirmantesRepository
{
    public async Task<DocuSignContactos> GetAsync(string cosabcli, CancellationToken ct)
    {
        var client = await context.Clientes.AsNoTracking().SingleOrDefaultAsync(c => c.Cosabcli == cosabcli, ct);
        if (client is null) return new([], "No se encontro el cliente.");
        var rawPhones = new List<string?>();
        if (Cliente.IsJointAccount(client.FgMancomunado))
            rawPhones = await context.ClientesApoderados.AsNoTracking()
                .Where(a => a.Cosabcli == cosabcli).Select(a => a.Nucel).ToListAsync(ct);
        return Resolve(client.Descli, client.FgMancomunado, client.Nucel, rawPhones,
            client.Nombres, client.Apepat, client.Apemat);
    }

    internal static DocuSignContactos Resolve(string? description, string? jointFlag, string? clientPhone,
        IReadOnlyList<string?> rawPhones, string? names = null, string? paternalSurname = null, string? maternalSurname = null)
    {
        var isJointAccount = Cliente.IsJointAccount(jointFlag);
        // Individual: nombre de la persona. Mancomunada: nombre compartido de la cuenta.
        var name = isJointAccount ? description?.Trim() ?? "" : string.Join(" ",
            new[] { names, paternalSurname, maternalSurname }
                .Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!.Trim()));
        if (name.Length is 0 or > 100)
            return new([], isJointAccount
                ? "El nombre de la cuenta mancomunada (descli) debe tener entre 1 y 100 caracteres."
                : "El nombre del cliente (nombres, apepat y apemat) debe tener entre 1 y 100 caracteres.");
        if (isJointAccount)
        {
            var phones = rawPhones.Select(NormalizePhone).ToArray();
            if (phones.Length is not (2 or 3) || phones.Any(p => p is null)
                || phones.Distinct(StringComparer.Ordinal).Count() != phones.Length)
                return new([], "La cuenta mancomunada necesita dos o tres celulares validos y distintos en clientes_apoderados.");
            Array.Sort(phones, StringComparer.Ordinal);
            return new(phones.Select((phone, index) => new DocuSignContacto($"Titular{index + 1}", name, phone!)).ToArray());
        }
        var phone = NormalizePhone(clientPhone);
        if (phone is null)
            return new([], "El cliente necesita un nombre y un celular valido en Opersab para recibir la firma por WhatsApp.");
        return new([new("Cliente", name, phone)]);
    }

    private static string? NormalizePhone(string? phone)
    {
        var digits = Regex.Replace(phone ?? "", "[\\s()+-]", "");
        if (Regex.IsMatch(digits, "^9[0-9]{8}$")) return "+51" + digits;
        if (Regex.IsMatch(digits, "^519[0-9]{8}$")) return "+" + digits;
        // Opersab tambien almacena el prefijo de Peru como 051.
        if (Regex.IsMatch(digits, "^0519[0-9]{8}$")) return "+" + digits[1..];
        return null;
    }
}
