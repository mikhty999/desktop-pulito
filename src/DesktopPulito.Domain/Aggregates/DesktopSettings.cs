namespace DesktopPulito.Domain;

/// <summary>Elementi aperti di recente dalla ricerca (al massimo 20, il più recente in testa).</summary>
public sealed class RecentItems
{
    public const int Capacity = 20;
    readonly List<string> items;

    public RecentItems(IEnumerable<string>? initial = null) =>
        items = (initial ?? []).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).Take(Capacity).ToList();

    public IReadOnlyList<string> Items => items;

    public void Touch(string path)
    {
        items.RemoveAll(s => string.Equals(s, path, StringComparison.OrdinalIgnoreCase));
        items.Insert(0, path);
        if (items.Count > Capacity) items.RemoveRange(Capacity, items.Count - Capacity);
    }

    /// <summary>Posizione nell'elenco (0 = più recente), o -1.</summary>
    public int RankOf(string path) => items.FindIndex(s => string.Equals(s, path, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Radice dell'aggregato "impostazioni": tutte le preferenze dell'utente, sempre valide.</summary>
public sealed class DesktopSettings
{
    public TransitionSpec Transition { get; set; } = TransitionSpec.Default;
    public AutoHidePolicy AutoHide { get; set; } = AutoHidePolicy.Default;
    public bool DoubleClickToggles { get; set; }
    public bool ZenMode { get; set; }
    public Hotkey ToggleHotkey { get; set; } = new(KeyModifiers.Ctrl | KeyModifiers.Alt, 0x44);   // Ctrl+Alt+D
    public Hotkey SearchHotkey { get; set; } = new(KeyModifiers.Ctrl | KeyModifiers.Alt, 0x46);   // Ctrl+Alt+F
    public Hotkey NextSceneHotkey { get; set; } = new(KeyModifiers.Ctrl | KeyModifiers.Alt, 0x53); // Ctrl+Alt+S
    public Appearance Appearance { get; set; } = Appearance.Default;
    public RecentItems Recent { get; set; } = new();
    public (int X, int Y)? SearchPosition { get; set; }
    public string? WeatherCity { get; set; }
    public string? CalendarIcsUrl { get; set; }

    /// <summary>Le scorciatoie non possono coincidere tra loro.</summary>
    public void SetHotkey(HotkeyAction action, Hotkey key)
    {
        if (!key.IsNone && !key.IsValid) throw new DomainException("Scorciatoia non valida: serve almeno Ctrl, Alt, Maiusc o Win.");
        foreach (HotkeyAction other in Enum.GetValues<HotkeyAction>())
            if (other != action && !key.IsNone && Get(other) == key)
                throw new DomainException("Questa combinazione è già usata da un'altra azione.");
        switch (action)
        {
            case HotkeyAction.ToggleIcons: ToggleHotkey = key; break;
            case HotkeyAction.Search: SearchHotkey = key; break;
            case HotkeyAction.NextScene: NextSceneHotkey = key; break;
        }
    }

    public Hotkey Get(HotkeyAction action) => action switch
    {
        HotkeyAction.ToggleIcons => ToggleHotkey,
        HotkeyAction.Search => SearchHotkey,
        _ => NextSceneHotkey,
    };

    public void SetCalendarUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) { CalendarIcsUrl = null; return; }
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out Uri? u) || (u.Scheme != "https" && u.Scheme != "http" && u.Scheme != "webcal"))
            throw new DomainException("Indirizzo del calendario non valido (serve un link iCal .ics).");
        CalendarIcsUrl = u.Scheme == "webcal" ? "https" + url.Trim()[6..] : url.Trim();
    }
}

