namespace DesktopPulito.Domain;

public sealed record AgendaEvent(string Title, DateTimeOffset Start, DateTimeOffset End, bool AllDay, string? Location);

