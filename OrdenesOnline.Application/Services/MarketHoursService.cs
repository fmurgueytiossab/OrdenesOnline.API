using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace OrdenesOnline.Application.Services;

public sealed class MarketHoursService(IConfiguration configuration, TimeProvider clock)
{
    private static readonly TimeSpan PeruOffset = TimeSpan.FromHours(-5);
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    public bool AppliesTo(string market) =>
        !string.Equals(configuration["MarketHours:ApplyToAllMarkets"], "false", StringComparison.OrdinalIgnoreCase)
        || market.Trim().ToUpperInvariant() is "BVL" or "01" or "LOCAL";

    public MarketHoursSnapshot Get()
    {
        var now = clock.GetUtcNow().ToOffset(PeruOffset);
        var today = DateOnly.FromDateTime(now.DateTime);
        var session = Session(today);
        var isOpen = session.TradingDay && now >= session.Open && now < session.Close;
        var validFor = session.TradingDay && now < session.Close ? today : NextDay(today);
        var nextOpen = session.TradingDay && now < session.Open ? session.Open : Session(NextDay(today)).Open;
        var transition = isOpen ? session.Close : nextOpen;
        return new(now, today, session.Open, session.Close, isOpen, nextOpen,
            validFor, transition, AppliesTo("CANACCORD"));
    }

    public string ClosedMessage(MarketHoursSnapshot state) =>
        $"El ingreso de órdenes está fuera de horario. Podrás registrar una orden el {state.NextOpenAt:dd/MM/yyyy} a las {state.NextOpenAt:HH:mm} (hora de Perú).";

    public bool TryResolveValidity(string market, string requested, MarketHoursSnapshot state, out string validity)
    {
        validity = requested;
        if (!AppliesTo(market)) return true;
        var text = requested.Trim();
        if (text.StartsWith("Hasta el ", StringComparison.OrdinalIgnoreCase))
        {
            if (!DateOnly.TryParseExact(text[9..], "dd/MM/yyyy", Culture, DateTimeStyles.None, out var until)
                || until < state.ValidForDate) return false;
            validity = $"Hasta el {until:dd/MM/yyyy}";
            return true;
        }
        if (text.StartsWith("Solo el ", StringComparison.OrdinalIgnoreCase))
        {
            if (!DateOnly.TryParseExact(text[8..], "dd/MM/yyyy", Culture, DateTimeStyles.None, out var day)
                || day != state.ValidForDate) return false;
        }
        else if (text.StartsWith("Por hoy", StringComparison.OrdinalIgnoreCase)
            || text.Equals("Día", StringComparison.OrdinalIgnoreCase) || text.Equals("Hoy", StringComparison.OrdinalIgnoreCase))
        {
            // Older clients must review the next-session date instead of silently rolling an order forward.
            if (state.ValidForDate != state.Today) return false;
        }
        else return false;
        validity = $"Solo el {state.ValidForDate:dd/MM/yyyy}";
        return true;
    }

    private DateOnly NextDay(DateOnly date)
    {
        for (var i = 0; i < 370; i++)
        {
            date = date.AddDays(1);
            if (Session(date).TradingDay) return date;
        }
        throw new InvalidOperationException("No hay una próxima sesión configurada en MarketHours.");
    }

    private (bool TradingDay, DateTimeOffset Open, DateTimeOffset Close) Session(DateOnly date)
    {
        var march = NthSunday(date.Year, 3, 2);
        var november = NthSunday(date.Year, 11, 1);
        var early = date >= march && date < november;
        var season = early ? "EarlySeason" : "LateSeason";
        var exception = configuration.GetSection($"MarketHours:Overrides:{date:yyyy-MM-dd}");
        var opening = configuration["MarketHours:OrderEntryOpen"] ?? "06:00";
        var closing = exception["Close"] ?? configuration[$"MarketHours:{season}:Close"] ?? (early ? "15:00" : "16:00");
        var open = TimeOnly.ParseExact(opening, "HH:mm", Culture);
        var close = TimeOnly.ParseExact(closing, "HH:mm", Culture);
        if (close <= open) throw new InvalidOperationException("MarketHours: el cierre debe ser posterior al inicio de recepción de órdenes.");
        var holiday = configuration.GetSection("MarketHours:ClosedDates").GetChildren()
            .Any(item => item.Value == date.ToString("yyyy-MM-dd", Culture));
        var trading = date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && !holiday;
        if (bool.TryParse(exception["IsClosed"], out var isClosed)) trading = !isClosed;
        return (trading, new(date.ToDateTime(open), PeruOffset), new(date.ToDateTime(close), PeruOffset));
    }

    private static DateOnly NthSunday(int year, int month, int occurrence)
    {
        var first = new DateOnly(year, month, 1);
        return first.AddDays(((7 - (int)first.DayOfWeek) % 7) + (occurrence - 1) * 7);
    }
}

public sealed record MarketHoursSnapshot(
    DateTimeOffset ServerNow, DateOnly Today, DateTimeOffset OpensAt, DateTimeOffset ClosesAt,
    bool IsOpen, DateTimeOffset NextOpenAt, DateOnly ValidForDate, DateTimeOffset NextTransitionAt,
    bool ApplyToAllMarkets);
