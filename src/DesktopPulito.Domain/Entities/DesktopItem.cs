namespace DesktopPulito.Domain;

public enum DesktopItemKind { File, Folder, Shortcut }

public sealed record DesktopItem(string Path, string Name, DesktopItemKind Kind)
{
    public string Key { get; } = SearchText.Normalize(Name);
}

