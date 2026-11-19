using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DesktopPulito.Application;
using DesktopPulito.Domain;

namespace DesktopPulito.Infrastructure.Online;

/// <summary>Un solo HttpClient per tutta l'app (riuso delle connessioni, niente esaurimento di socket).</summary>
public static class Http
{
    public static readonly HttpClient Client = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        AutomaticDecompression = System.Net.DecompressionMethods.All,
    })
    { Timeout = TimeSpan.FromSeconds(12) };

    static Http() => Client.DefaultRequestHeaders.UserAgent.ParseAdd("DesktopPulito/2.0 (+open source)");
}

/// <summary>
/// Meteo da Open-Meteo (https://open-meteo.com): gratuito, open source, senza chiave né account.
/// Geocodifica del nome della città con la loro API di geocoding (stesse condizioni).
/// </summary>
public sealed class OpenMeteoWeather(HttpClient? http = null) : IWeatherProvider
{
    readonly HttpClient http = http ?? Http.Client;

    public async Task<GeoLocation?> FindPlaceAsync(string name, string language, CancellationToken ct)
    {
        string url = "https://geocoding-api.open-meteo.com/v1/search?count=1&format=json&name=" + Uri.EscapeDataString(name.Trim()) +
                     "&language=" + Uri.EscapeDataString(language);
        using JsonDocument doc = JsonDocument.Parse(await http.GetStringAsync(url, ct));
        return ParsePlace(doc.RootElement);
    }

    public async Task<WeatherSnapshot> GetCurrentAsync(GeoLocation place, CancellationToken ct)
    {
        string url = string.Format(CultureInfo.InvariantCulture,
            "https://api.open-meteo.com/v1/forecast?latitude={0:0.####}&longitude={1:0.####}&current=temperature_2m,weather_code,is_day&timezone=auto",
            place.Latitude, place.Longitude);
        using JsonDocument doc = JsonDocument.Parse(await http.GetStringAsync(url, ct));
        return ParseCurrent(doc.RootElement, place);
    }

    internal static GeoLocation? ParsePlace(JsonElement root)
    {
        if (!root.TryGetProperty("results", out JsonElement res) || res.GetArrayLength() == 0) return null;
        JsonElement r = res[0];
        return GeoLocation.Create(r.GetProperty("latitude").GetDouble(), r.GetProperty("longitude").GetDouble(), r.GetProperty("name").GetString() ?? "");
    }

    internal static WeatherSnapshot ParseCurrent(JsonElement root, GeoLocation place)
    {
        JsonElement c = root.GetProperty("current");
        DateTimeOffset at = DateTimeOffset.Now;
        if (c.TryGetProperty("time", out JsonElement t) && DateTime.TryParse(t.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime local))
        {
            int offset = root.TryGetProperty("utc_offset_seconds", out JsonElement o) ? o.GetInt32() : 0;
            at = new DateTimeOffset(local, TimeSpan.FromSeconds(offset));
        }
        return new WeatherSnapshot(place, c.GetProperty("temperature_2m").GetDouble(),
            WeatherSnapshot.FromWmo(c.GetProperty("weather_code").GetInt32()),
            c.TryGetProperty("is_day", out JsonElement d) && d.GetInt32() == 1, at);
    }
}

/// <summary>
/// Calendario da link iCal (.ics). In Google Calendar: Impostazioni → il tuo calendario → "Indirizzo segreto in formato iCal".
/// Funziona anche con Outlook, Apple, Proton, Nextcloud. Nessun account sviluppatore, nessuna verifica: gratuito.
/// Gestisce eventi singoli (le ricorrenze RRULE arriveranno in seguito).
/// </summary>
public sealed class IcsCalendarFeed(HttpClient? http = null) : ICalendarFeed
{
    readonly HttpClient http = http ?? Http.Client;

    public async Task<IReadOnlyList<AgendaEvent>> LoadAsync(Uri ics, CancellationToken ct) =>
        Parse(await http.GetStringAsync(ics, ct));

    public static IReadOnlyList<AgendaEvent> Parse(string text)
    {
        List<AgendaEvent> list = [];
        string? title = null, loc = null; DateTimeOffset? start = null, end = null; bool allDay = false, inEvent = false;
        foreach (string line in Unfold(text))
        {
            if (line == "BEGIN:VEVENT") { inEvent = true; title = loc = null; start = end = null; allDay = false; continue; }
            if (line == "END:VEVENT")
            {
                if (inEvent && start is { } s) list.Add(new AgendaEvent(title ?? "", s, end ?? (allDay ? s.AddDays(1) : s.AddHours(1)), allDay, loc));
                inEvent = false; continue;
            }
            if (!inEvent) continue;
            int colon = line.IndexOf(':');
            if (colon < 0) continue;
            string head = line[..colon], value = line[(colon + 1)..];
            string name = head.Split(';')[0].ToUpperInvariant();
            switch (name)
            {
                case "SUMMARY": title = UnescapeText(value); break;
                case "LOCATION": loc = UnescapeText(value); break;
                case "DTSTART": start = ParseDate(value, head, out allDay); break;
                case "DTEND": end = ParseDate(value, head, out _); break;
            }
        }
        return list;
    }

    // RFC 5545: le righe lunghe proseguono sulla riga successiva che inizia con spazio o tab
    static IEnumerable<string> Unfold(string text)
    {
        StringBuilder cur = new();
        using StringReader r = new(text);
        string? l;
        while ((l = r.ReadLine()) != null)
        {
            if (l.Length > 0 && (l[0] == ' ' || l[0] == '\t')) { cur.Append(l, 1, l.Length - 1); continue; }
            if (cur.Length > 0) yield return cur.ToString();
            cur.Clear().Append(l);
        }
        if (cur.Length > 0) yield return cur.ToString();
    }

    static DateTimeOffset? ParseDate(string v, string head, out bool allDay)
    {
        allDay = head.Contains("VALUE=DATE", StringComparison.OrdinalIgnoreCase) && !head.Contains("VALUE=DATE-TIME", StringComparison.OrdinalIgnoreCase) || v.Length == 8;
        if (allDay && DateTime.TryParseExact(v, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime d))
            return new DateTimeOffset(d, TimeZoneInfo.Local.GetUtcOffset(d));
        if (v.EndsWith('Z') && DateTime.TryParseExact(v, "yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime u))
            return new DateTimeOffset(u, TimeSpan.Zero).ToLocalTime();
        if (DateTime.TryParseExact(v, "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime l))
            return new DateTimeOffset(l, TimeZoneInfo.Local.GetUtcOffset(l));   // TZID: trattato come ora locale
        return null;
    }

    static string UnescapeText(string s) => s.Replace("\\n", " ").Replace("\\N", " ").Replace("\\,", ",").Replace("\\;", ";").Replace("\\\\", "\\");
}
