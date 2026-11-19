using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using DesktopPulito.Application;
using DesktopPulito.Domain;
using Microsoft.Win32;

namespace DesktopPulito.Infrastructure.Windows;

/// <summary>Icone del desktop di Windows + motore delle transizioni (strati composti dalla GPU).</summary>
public sealed class WindowsDesktopIcons : IDesktopIcons
{
    public bool AreVisible => Desk.IconsVisible();

    public void SetVisible(bool visible, TransitionSpec transition)
    {
        if (Desk.IconsVisible() == visible) return;
        int ms = transition.DurationMs(visible);
        if (ms > 0) Fader.Run(visible, ms, (int)transition.Style);
        else Desk.ToggleRaw();
        if (visible) Desk.RefreshIconRects();
    }

    public void SetVisibleNow(bool visible)
    {
        if (Desk.IconsVisible() != visible) Desk.ToggleRaw();
    }
}

/// <summary>Cambi di scena "sotto copertura" con qualsiasi effetto (anche quelli sulla scheda video).</summary>
public sealed class WindowsDesktopTransitions : IDesktopTransitions
{
    public void Play(TransitionSpec transition, bool iconsVisibleAfter, Action change)
    {
        int ms = transition.DurationMs(iconsVisibleAfter);
        Fader.Run(iconsVisibleAfter, ms, (int)transition.Style, delegate
        {
            change();
            // il nuovo sfondo viene disegnato da Explorer in modo asincrono: aspettiamo che sia a schermo
            for (int i = 0; i < 6; i++) N.DwmFlush();
        }, iconsOnly: false);
        if (iconsVisibleAfter) Desk.RefreshIconRects();
    }
}

/// <summary>Sfondo del desktop (stesso meccanismo del Pannello di controllo, vale per tutti i monitor).</summary>
public sealed class WindowsWallpaper : IWallpaper
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern bool SystemParametersInfo(uint action, uint param, StringBuilder value, uint winIni);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern bool SystemParametersInfo(uint action, uint param, string value, uint winIni);

    public string? Current
    {
        get
        {
            StringBuilder sb = new(520);
            return SystemParametersInfo(0x0073, (uint)sb.Capacity, sb, 0) ? sb.ToString() : null;   // SPI_GETDESKWALLPAPER
        }
    }

    public void Set(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Immagine di sfondo non trovata.", path);
        SystemParametersInfo(0x0014, 0, path, 0x01 | 0x02);   // SPI_SETDESKWALLPAPER, aggiorna e notifica
    }
}

/// <summary>Elementi del desktop (utente + pubblico) per la ricerca.</summary>
public sealed class WindowsDesktopItems : IDesktopItemSource
{
    public IReadOnlyList<DesktopItem> Load()
    {
        List<DesktopItem> list = [];
        foreach (DItem d in DesktopItems.Load())
        {
            string ext = Path.GetExtension(d.Path).ToLowerInvariant();
            DesktopItemKind kind = d.Dir ? DesktopItemKind.Folder : ext is ".lnk" or ".url" ? DesktopItemKind.Shortcut : DesktopItemKind.File;
            list.Add(new DesktopItem(d.Path, d.Name, kind));
        }
        return list;
    }
}

/// <summary>Avvio automatico: copia l'app in %LOCALAPPDATA%\Programs\DesktopPulito e la registra all'avvio di Windows.</summary>
public static class Autostart
{
    const string RUN = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string NAME = "DesktopPulito";
    static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "DesktopPulito");

    public static bool IsOn()
    {
        using RegistryKey? k = Registry.CurrentUser.OpenSubKey(RUN);
        return k?.GetValue(NAME) != null;
    }

    public static void Set(bool on)
    {
        using RegistryKey k = Registry.CurrentUser.CreateSubKey(RUN);
        if (!on) { k.DeleteValue(NAME, false); return; }
        string exe = Environment.ProcessPath ?? System.Windows.Forms.Application.ExecutablePath;
        string srcDir = Path.GetDirectoryName(exe)!;
        if (!string.Equals(Path.GetFullPath(srcDir).TrimEnd('\\'), Path.GetFullPath(Dir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
        {
            // l'app .NET è formata da più file: copiamo l'intera cartella
            Directory.CreateDirectory(Dir);
            foreach (string f in Directory.GetFiles(srcDir))
                try { File.Copy(f, Path.Combine(Dir, Path.GetFileName(f)), true); } catch (IOException) { }
            exe = Path.Combine(Dir, Path.GetFileName(exe));
        }
        k.SetValue(NAME, "\"" + exe + "\" /tray");
    }

    /// <summary>Rimozione completa: avvio automatico, impostazioni e copia installata (cancellata dopo l'uscita).</summary>
    public static void Uninstall()
    {
        Set(false);
        try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\DesktopPulito", false); } catch { }
        try { Directory.Delete(Storage.AppData.Folder, true); } catch { }
        if (Directory.Exists(Dir))
            try
            {
                Process.Start(new ProcessStartInfo("cmd.exe", "/c timeout /t 2 /nobreak >nul & rmdir /s /q \"" + Dir + "\"")
                { CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden });
            }
            catch { }
    }
}
