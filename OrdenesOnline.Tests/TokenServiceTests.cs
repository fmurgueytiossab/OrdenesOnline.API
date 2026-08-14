using Microsoft.Extensions.Configuration;
using OrdenesOnline.Application.Services;
using System.IdentityModel.Tokens.Jwt;

namespace OrdenesOnline.Tests;

public sealed class TokenServiceTests
{
    [Fact]
    public void AccessToken_UsesExpectedAudienceAndPurpose()
    {
        var service = CreateService();
        var handler = new JwtSecurityTokenHandler();

        var access = handler.ReadJwtToken(service.GenerateAccessToken("persona@example.com", 42));

        Assert.Contains("ClientesFrontend", access.Audiences);
        Assert.Equal("access", access.Claims.Single(claim => claim.Type == "token_use").Value);
        Assert.Equal("42", access.Subject);
    }

    private static TokenService CreateService()
    {
        var settings = new Dictionary<string, string?>
        {
            ["Jwt:Key"] = "a-development-test-key-with-at-least-32-bytes",
            ["Jwt:Issuer"] = "ClientesAPI",
            ["Jwt:Audience"] = "ClientesFrontend",
            ["Jwt:AccessTokenMinutes"] = "30"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        return new TokenService(configuration);
    }
}
