using Microsoft.Extensions.Configuration;
using OrdenesOnline.Application.Services;
using OrdenesOnline.Tests.TestDoubles;

namespace OrdenesOnline.Tests;

public sealed class ActionTokenServiceTests
{
    [Fact]
    public async Task ProposalReviewToken_IsStoredHashedAndBoundToUserAndProposal()
    {
        var repository = new FakeActionTokenRepository();
        var service = CreateService(repository);

        var issued = await service.CreateProposalReviewTokenAsync(7, 123);
        var stored = Assert.Single(repository.Tokens);

        Assert.NotEqual(issued.Value, stored.TokenHash);
        Assert.Equal(64, stored.TokenHash.Length);
        Assert.Equal(7, stored.UserId);
        Assert.Equal(123, stored.PropuestaId);
        Assert.Equal(ActionTokenService.ProposalReviewType, stored.Type);
        Assert.NotNull(await service.ValidateAsync(issued.Value, ActionTokenService.ProposalReviewType));
        Assert.Null(await service.ValidateAsync(issued.Value, ActionTokenService.PasswordResetType));
    }

    [Fact]
    public async Task UsedToken_CannotBeValidatedAgain()
    {
        var repository = new FakeActionTokenRepository();
        var service = CreateService(repository);
        var issued = await service.CreatePasswordResetTokenAsync(7);

        Assert.True(await service.TryMarkUsedAsync(issued.TokenId));

        Assert.Null(await service.ValidateAsync(issued.Value, ActionTokenService.PasswordResetType));
        Assert.False(await service.TryMarkUsedAsync(issued.TokenId));
    }

    private static ActionTokenService CreateService(FakeActionTokenRepository repository)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ActionTokens:PasswordResetMinutes"] = "15",
                ["ActionTokens:ProposalReviewMinutes"] = "1440"
            })
            .Build();

        return new ActionTokenService(repository, configuration);
    }
}
