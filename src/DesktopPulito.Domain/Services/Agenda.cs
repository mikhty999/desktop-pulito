namespace DesktopPulito.Domain;

/// <summary>Gli impegni rilevanti a partire da "adesso": in corso o futuri, in ordine.</summary>
public static class Agenda
{
    public static IReadOnlyList<AgendaEvent> Upcoming(IEnumerable<AgendaEvent> events, DateTimeOffset now, int max) =>
        events.Where(e => e.End > now)
              .OrderBy(e => e.Start)
              .ThenBy(e => e.Title, StringComparer.CurrentCultureIgnoreCase)
              .Take(Math.Max(0, max))
              .ToList();
}

