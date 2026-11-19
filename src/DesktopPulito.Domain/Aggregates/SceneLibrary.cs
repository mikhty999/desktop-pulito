namespace DesktopPulito.Domain;

/// <summary>Aggregato delle scene: almeno una, nomi unici, una sola attiva.</summary>
public sealed class SceneLibrary
{
    readonly List<Scene> scenes = [];
    public IReadOnlyList<Scene> Scenes => scenes;
    public SceneId ActiveId { get; private set; }
    public Scene Active => scenes.First(s => s.Id == ActiveId);

    public SceneLibrary(IEnumerable<Scene> initial, SceneId? active = null)
    {
        foreach (Scene s in initial) Add(s);
        if (scenes.Count == 0) throw new DomainException("Serve almeno una scena.");
        ActiveId = active is { } a && scenes.Any(s => s.Id == a) ? a : scenes[0].Id;
    }

    /// <summary>Scene iniziali: il desktop di sempre e il desktop pulito.</summary>
    public static SceneLibrary CreateDefault(bool italian)
    {
        Scene work = new(SceneId.New(), italian ? "Lavoro" : "Work") { ShowIcons = true };
        Scene clean = new(SceneId.New(), italian ? "Pulito" : "Clean") { ShowIcons = false };
        clean.PlaceWidget(WidgetPlacement.Create(WidgetKind.Clock, 0.78, 0.12));
        return new SceneLibrary([work, clean]);
    }

    public void Add(Scene s)
    {
        if (scenes.Any(x => string.Equals(x.Name, s.Name, StringComparison.CurrentCultureIgnoreCase)))
            throw new DomainException($"Esiste già una scena chiamata \"{s.Name}\".");
        scenes.Add(s);
    }

    public void Remove(SceneId id)
    {
        if (scenes.Count == 1) throw new DomainException("Non puoi eliminare l'ultima scena.");
        int i = scenes.FindIndex(s => s.Id == id);
        if (i < 0) return;
        scenes.RemoveAt(i);
        if (ActiveId == id) ActiveId = scenes[Math.Min(i, scenes.Count - 1)].Id;
    }

    public Scene Activate(SceneId id)
    {
        if (!scenes.Any(s => s.Id == id)) throw new DomainException("Scena inesistente.");
        ActiveId = id;
        return Active;
    }

    /// <summary>La scena successiva (in ciclo), per la scorciatoia "scena successiva".</summary>
    public Scene Next()
    {
        int i = scenes.FindIndex(s => s.Id == ActiveId);
        return scenes[(i + 1) % scenes.Count];
    }
}

