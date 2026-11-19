namespace DesktopPulito.Domain;

/// <summary>
/// Una scena è un "volto" del desktop: sfondo, icone visibili o no, widget e la transizione con cui si entra.
/// Esempio: "Lavoro" (icone, nessun widget) → "Relax" (sfondo foresta, orologio e meteo, niente icone).
/// </summary>
public sealed class Scene
{
    public SceneId Id { get; }
    public string Name { get; private set; }
    public string? WallpaperPath { get; set; }
    public bool ShowIcons { get; set; }
    public TransitionSpec? EnterWith { get; set; }
    readonly List<WidgetPlacement> widgets = [];
    public IReadOnlyList<WidgetPlacement> Widgets => widgets;

    public Scene(SceneId id, string name)
    {
        Id = id;
        Name = ValidName(name);
    }

    public void Rename(string name) => Name = ValidName(name);

    public void PlaceWidget(WidgetPlacement w)
    {
        widgets.RemoveAll(x => x.Kind == w.Kind);   // un widget per tipo, per scena
        widgets.Add(w);
    }

    public void RemoveWidget(WidgetKind kind) => widgets.RemoveAll(x => x.Kind == kind);

    static string ValidName(string name)
    {
        string n = (name ?? "").Trim();
        if (n.Length == 0) throw new DomainException("La scena deve avere un nome.");
        if (n.Length > 40) throw new DomainException("Il nome della scena è troppo lungo (massimo 40 caratteri).");
        return n;
    }
}

