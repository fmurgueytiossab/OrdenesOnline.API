using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace OrdenesOnline.Application.Services;

public sealed class TokenService
{
    private const string TokenUseClaim = "token_use";
    private const string AccessTokenUse = "access";
    private const string PasswordResetTokenUse = "password_reset";

    private readonly string _issuer;
    private readonly string _accessAudience;
    private readonly string _passwordResetAudience;
    private readonly SymmetricSecurityKey _signingKey;
    private readonly int _accessTokenMinutes;
    private readonly int _passwordResetTokenMinutes;

    public TokenService(IConfiguration configuration)
    {
        _issuer = GetRequiredSetting(configuration, "Jwt:Issuer");
        _accessAudience = GetRequiredSetting(configuration, "Jwt:Audience");
        _passwordResetAudience = GetRequiredSetting(configuration, "Jwt:PasswordResetAudience");

        var key = GetRequiredSetting(configuration, "Jwt:Key");
        if (Encoding.UTF8.GetByteCount(key) < 32)
        {
            throw new InvalidOperationException("Jwt:Key debe tener al menos 32 bytes.");
        }

        _signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
        _accessTokenMinutes = GetPositiveInt(configuration, "Jwt:AccessTokenMinutes", 30);
        _passwordResetTokenMinutes = GetPositiveInt(configuration, "Jwt:PasswordResetTokenMinutes", 15);
    }

    public string GenerateAccessToken(string email, int userId) =>
        GenerateToken(email, userId, _accessAudience, AccessTokenUse, _accessTokenMinutes);

    public string GeneratePasswordResetToken(string email, int userId) =>
        GenerateToken(email, userId, _passwordResetAudience, PasswordResetTokenUse, _passwordResetTokenMinutes);

    public string? ValidatePasswordResetToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var tokenHandler = new JwtSecurityTokenHandler
        {
            MapInboundClaims = false
        };

        try
        {
            var principal = tokenHandler.ValidateToken(token, CreateValidationParameters(_passwordResetAudience), out var validatedToken);

            if (validatedToken is not JwtSecurityToken jwtToken ||
                !string.Equals(jwtToken.Header.Alg, SecurityAlgorithms.HmacSha256, StringComparison.Ordinal) ||
                !string.Equals(principal.FindFirst(TokenUseClaim)?.Value, PasswordResetTokenUse, StringComparison.Ordinal))
            {
                return null;
            }

            return principal.FindFirst(JwtRegisteredClaimNames.Email)?.Value;
        }
        catch (SecurityTokenException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private string GenerateToken(string email, int userId, string audience, string tokenUse, int lifetimeMinutes)
    {
        var now = DateTime.UtcNow;
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, email),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new Claim(JwtRegisteredClaimNames.Iat, EpochTime.GetIntDate(now).ToString(), ClaimValueTypes.Integer64),
            new Claim(TokenUseClaim, tokenUse)
        };

        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: audience,
            claims: claims,
            notBefore: now,
            expires: now.AddMinutes(lifetimeMinutes),
            signingCredentials: new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private TokenValidationParameters CreateValidationParameters(string audience) => new()
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = _signingKey,
        ValidateIssuer = true,
        ValidIssuer = _issuer,
        ValidateAudience = true,
        ValidAudience = audience,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero
    };

    private static string GetRequiredSetting(IConfiguration configuration, string key)
    {
        var value = configuration[key];
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException($"Falta la configuración obligatoria '{key}'.");
    }

    private static int GetPositiveInt(IConfiguration configuration, string key, int fallback)
    {
        return int.TryParse(configuration[key], out var value) && value > 0 ? value : fallback;
    }
}
