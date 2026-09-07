namespace OrdenesOnline.Domain.entities;

public static class PropuestaTrackingDay
{
    // Fecha_Registro is stored in UTC. Peru uses UTC-05 throughout the year.
    public static readonly TimeSpan PeruOffset = TimeSpan.FromHours(-5);

    public static (DateTime StartUtc, DateTime EndUtc) GetUtcRange(DateTimeOffset now)
    {
        var localDate = now.ToOffset(PeruOffset).Date;
        var start = new DateTimeOffset(localDate, PeruOffset).UtcDateTime;
        return (start, start.AddDays(1));
    }

    public static DateTime ToPeru(DateTime registeredUtc) =>
        DateTime.SpecifyKind(registeredUtc, DateTimeKind.Utc).Add(PeruOffset);
}
