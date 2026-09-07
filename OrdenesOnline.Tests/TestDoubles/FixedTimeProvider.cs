namespace OrdenesOnline.Tests.TestDoubles;

public sealed class FixedTimeProvider(string utc) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => DateTimeOffset.Parse(utc);
}
