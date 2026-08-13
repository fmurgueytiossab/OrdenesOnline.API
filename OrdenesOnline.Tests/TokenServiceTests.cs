using Microsoft.Extensions.Configuration;
using OrdenesOnline.Application.Services;
using System.IdentityModel.Tokens.Jwt;

namespace OrdenesOnline.Tests;

public sealed class TokenServiceTests
{
    [Fact]
    public void PasswordResetToken_IsAcceptedOnlyByPasswordResetFlow()
    {
        var service = CreateService();

        var token = service.GeneratePasswordResetToken("persona@example.com", 42);

        Assert.Equal("persona@example.com", service.ValidatePasswordResetToken(token));
    }

    [Fact]
    public void AccessToken_IsRejectedByPasswordResetFlow()
    {
        var service = CreateService();

        var token = service.GenerateAccessToken("persona@example.com", 42);

        Assert.Null(service.ValidatePasswordResetToken(token));
    }

    [Fact]
    public void GeneratedTokens_UseDifferentAudiencesAndPurposes()
    {
        var service = CreateService();
        var handler = new JwtSecurityTokenHandler();

        var access = handler.ReadJwtToken(service.GenerateAccessToken("persona@example.com", 42));
        var reset = handler.ReadJwtToken(service.GeneratePasswordResetToken("persona@example.com", 42));

        Assert.Contains("ClientesFrontend", access.Audiences);
        Assert.Contains("ClientesPasswordReset", reset.Audiences);
        Assert.Equal("access", access.Claims.Single(claim => claim.Type == "token_use").Value);
        Assert.Equal("password_reset", reset.Claims.Single(claim => claim.Type == "token_use").Value);
    }

    private static TokenService CreateService()
    {
        var settings = new Dictionary<string, string?>
        {
            ["Jwt:Key"] = "a-development-test-key-with-at-least-32-bytes",
            ["Jwt:Issuer"] = "ClientesAPI",
            ["Jwt:Audience"] = "ClientesFrontend",
            ["Jwt:PasswordResetAudience"] = "ClientesPasswordReset",
            ["Jwt:AccessTokenMinutes"] = "30",
            ["Jwt:PasswordResetTokenMinutes"] = "15"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        return new TokenService(configuration);
    }
}
