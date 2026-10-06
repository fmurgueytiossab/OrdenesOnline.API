using System.Security.Cryptography;
using System.Text;

namespace OrdenesOnline.Application.Services;

public static class DocuSignHmac
{
    public static bool Validate(byte[] body, string secret, IEnumerable<string> signatures)
    {
        if (string.IsNullOrWhiteSpace(secret)) return false;
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body);
        foreach (var signature in signatures)
        {
            try
            {
                var actual = Convert.FromBase64String(signature);
                if (CryptographicOperations.FixedTimeEquals(expected, actual)) return true;
            }
            catch (FormatException) { }
        }
        return false;
    }
}
