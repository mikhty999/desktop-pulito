namespace DesktopPulito.Domain;

[Flags]
public enum KeyModifiers { None = 0, Alt = 1, Ctrl = 2, Shift = 4, Win = 8 }

/// <summary>Scorciatoia globale. Valida se ha almeno un modificatore, oppure è un tasto funzione.</summary>
public readonly record struct Hotkey(KeyModifiers Modifiers, int VirtualKey)
{
    public static Hotkey None => default;
    public bool IsNone => VirtualKey == 0;
    public bool IsValid => !IsNone && (Modifiers != KeyModifiers.None || (VirtualKey >= 0x70 && VirtualKey <= 0x87));

    /// <summary>Formato compatto storico: (modificatori &lt;&lt; 16) | tasto.</summary>
    public int Packed => IsNone ? 0 : ((int)Modifiers << 16) | VirtualKey;
    public static Hotkey FromPacked(int v) => v <= 0 ? None : new((KeyModifiers)((v >> 16) & 0xF), v & 0xFFFF);
}

public enum HotkeyAction { ToggleIcons, Search, NextScene }

