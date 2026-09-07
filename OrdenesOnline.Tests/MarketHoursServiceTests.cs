using Microsoft.Extensions.Configuration;
using OrdenesOnline.Application.Services;
using OrdenesOnline.Tests.TestDoubles;

namespace OrdenesOnline.Tests;

public sealed class MarketHoursServiceTests
{
    [Theory]
    [InlineData("2026-09-07T13:29:59Z", false, "08:30", "15:00")]
    [InlineData("2026-09-07T13:30:00Z", true, "08:30", "15:00")]
    [InlineData("2026-09-07T19:59:59Z", true, "08:30", "15:00")]
    [InlineData("2026-09-07T20:00:00Z", false, "08:30", "15:00")]
    [InlineData("2026-11-02T14:29:59Z", false, "09:30", "16:00")]
    [InlineData("2026-11-02T14:30:00Z", true, "09:30", "16:00")]
    [InlineData("2026-11-02T20:59:59Z", true, "09:30", "16:00")]
    [InlineData("2026-11-02T21:00:00Z", false, "09:30", "16:00")]
    [InlineData("2026-03-06T15:00:00Z", true, "09:30", "16:00")]
    [InlineData("2026-03-09T15:00:00Z", true, "08:30", "15:00")]
    public void EnforcesOpeningAndClosingWithSeasonChanges(string utc, bool open, string opening, string closing)
    {
        var state = Service(utc).Get();
        Assert.Equal(open, state.IsOpen);
        Assert.Equal(opening, state.OpensAt.ToString("HH:mm"));
        Assert.Equal(closing, state.ClosesAt.ToString("HH:mm"));
        Assert.Equal(TimeSpan.FromHours(-5), state.ServerNow.Offset);
    }

    [Theory]
    [InlineData("2026-09-07T12:00:00Z", "2026-09-07")]
    [InlineData("2026-09-07T20:00:00Z", "2026-09-08")]
    [InlineData("2026-09-12T03:00:00Z", "2026-09-14")]
    [InlineData("2026-09-12T15:00:00Z", "2026-09-14")]
    [InlineData("2026-10-07T20:00:00Z", "2026-10-09")]
    public void UsesPeruDateAndSkipsWeekendsAndConfiguredHolidays(string utc, string expected)
    {
        var service = Service(utc, new() { ["MarketHours:ClosedDates:0"] = "2026-10-08" });
        Assert.Equal(DateOnly.Parse(expected), service.Get().ValidForDate);
    }

    [Fact]
    public void SupportsSpecialClosingHoursWithoutCodeChanges()
    {
        var service = Service("2026-09-07T18:00:00Z", new()
        {
            ["MarketHours:Overrides:2026-09-07:Close"] = "13:00"
        });
        Assert.False(service.Get().IsOpen);
        Assert.Equal(new DateOnly(2026, 9, 8), service.Get().ValidForDate);
    }

    [Theory]
    [InlineData("Por hoy : 07/09/2026", false)]
    [InlineData("Solo el 07/09/2026", false)]
    [InlineData("Solo el 08/09/2026", true)]
    [InlineData("Hasta el 07/09/2026", false)]
    [InlineData("Hasta el 08/09/2026", true)]
    [InlineData("Hasta el 10/09/2026", true)]
    [InlineData("Solo el 31/02/2026", false)]
    public void ValidatesPersistedValidityAtTheCutoff(string validity, bool accepted)
    {
        var service = Service("2026-09-07T20:00:00Z");
        Assert.Equal(accepted, service.TryResolveValidity("BVL", validity, service.Get(), out _));
    }

    [Theory]
    [InlineData("BVL")]
    [InlineData("Canaccord")]
    [InlineData("Euroclear")]
    public void AppliesToEveryPolicy(string market) => Assert.True(Service("2026-09-07T20:00:00Z").AppliesTo(market));

    private static MarketHoursService Service(string utc, Dictionary<string, string?>? settings = null) =>
        new(new ConfigurationBuilder().AddInMemoryCollection(settings ?? []).Build(), new FixedTimeProvider(utc));
}
