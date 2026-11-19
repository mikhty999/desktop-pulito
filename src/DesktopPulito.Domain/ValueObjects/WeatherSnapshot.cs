namespace DesktopPulito.Domain;

public enum WeatherCondition { Clear, PartlyCloudy, Cloudy, Fog, Drizzle, Rain, Snow, Showers, Thunderstorm, Unknown }

public sealed record WeatherSnapshot(GeoLocation Place, double TemperatureC, WeatherCondition Condition, bool IsDay, DateTimeOffset ObservedAt)
{
    /// <summary>Traduce i codici meteo WMO (standard internazionale usato da Open-Meteo).</summary>
    public static WeatherCondition FromWmo(int code) => code switch
    {
        0 => WeatherCondition.Clear,
        1 or 2 => WeatherCondition.PartlyCloudy,
        3 => WeatherCondition.Cloudy,
        45 or 48 => WeatherCondition.Fog,
        >= 51 and <= 57 => WeatherCondition.Drizzle,
        >= 61 and <= 67 => WeatherCondition.Rain,
        >= 71 and <= 77 => WeatherCondition.Snow,
        >= 80 and <= 86 => WeatherCondition.Showers,
        >= 95 and <= 99 => WeatherCondition.Thunderstorm,
        _ => WeatherCondition.Unknown,
    };

    public bool IsStale(DateTimeOffset now, TimeSpan maxAge) => now - ObservedAt > maxAge;
}

