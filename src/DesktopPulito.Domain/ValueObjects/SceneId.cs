namespace DesktopPulito.Domain;

public readonly record struct SceneId(Guid Value)
{
    public static SceneId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString("N");
}

