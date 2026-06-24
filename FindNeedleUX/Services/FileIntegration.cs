using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace FindNeedleUX.Services;

/// <summary>
/// Registers / unregisters Find Needle's Explorer integrations under <c>HKCU\Software\Classes</c> so
/// they can be toggled from settings:
///   • an "Open with → Find Needle" entry (a ProgId added to each type's OpenWithProgids), and
///   • a right-click "Open in Find Needle" verb (per type, under SystemFileAssociations).
/// Effective for the installed/unpackaged build. A packaged (Store) MSIX virtualizes these writes, so
/// there the manifest fileTypeAssociation provides "Open with" and the toggles are persisted intent
/// (a future IExplorerCommand handler honors the context-menu one). All ops are best-effort — they log
/// and swallow rather than throw, so a settings toggle never crashes the app.
/// </summary>
public static class FileIntegration
{
    private const string ProgId = "FindNeedle.LogFile";
    private const string Verb = "OpenInFindNeedle";

    /// <summary>True when running as a packaged (MSIX) app — registry writes here are virtualized and
    /// won't reach the real shell, so we skip them and let the manifest/handler drive integration.</summary>
    public static bool IsPackaged
    {
        get
        {
            try { return global::Windows.ApplicationModel.Package.Current != null; }
            catch { return false; }
        }
    }

    private static string ExePath =>
        Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "";

    public static void SetOpenWith(bool on)
    {
        try
        {
            if (IsPackaged) return; // provided by the manifest on the Store build
            if (on) RegisterOpenWith(); else UnregisterOpenWith();
            NotifyShell();
        }
        catch (Exception ex) { Debug.WriteLine($"FileIntegration.SetOpenWith failed: {ex.Message}"); }
    }

    public static void SetContextMenu(bool on)
    {
        try
        {
            if (IsPackaged) return;
            if (on) RegisterContextMenu(); else UnregisterContextMenu();
            NotifyShell();
        }
        catch (Exception ex) { Debug.WriteLine($"FileIntegration.SetContextMenu failed: {ex.Message}"); }
    }

    private static void RegisterOpenWith()
    {
        var exe = ExePath;
        if (string.IsNullOrEmpty(exe)) return;
        using (var prog = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}"))
        {
            prog.SetValue("", "Find Needle log");
            using (var icon = prog.CreateSubKey("DefaultIcon")) icon.SetValue("", $"\"{exe}\",0");
            using (var cmd = prog.CreateSubKey(@"shell\open\command")) cmd.SetValue("", $"\"{exe}\" \"%1\"");
        }
        foreach (var ext in FileAssociations.Extensions)
            using (var k = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ext}\OpenWithProgids"))
                k.SetValue(ProgId, Array.Empty<byte>(), RegistryValueKind.None);
    }

    private static void UnregisterOpenWith()
    {
        foreach (var ext in FileAssociations.Extensions)
        {
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{ext}\OpenWithProgids", writable: true);
                k?.DeleteValue(ProgId, throwOnMissingValue: false);
            }
            catch { /* best-effort */ }
        }
        try { Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{ProgId}", throwOnMissingSubKey: false); }
        catch { /* best-effort */ }
    }

    private static void RegisterContextMenu()
    {
        var exe = ExePath;
        if (string.IsNullOrEmpty(exe)) return;
        foreach (var ext in FileAssociations.Extensions)
        {
            using var shell = Registry.CurrentUser.CreateSubKey(
                $@"Software\Classes\SystemFileAssociations\{ext}\shell\{Verb}");
            shell.SetValue("MUIVerb", "Open in Find Needle");
            shell.SetValue("Icon", $"\"{exe}\",0");
            using var cmd = shell.CreateSubKey("command");
            cmd.SetValue("", $"\"{exe}\" \"%1\"");
        }
    }

    private static void UnregisterContextMenu()
    {
        foreach (var ext in FileAssociations.Extensions)
        {
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(
                    $@"Software\Classes\SystemFileAssociations\{ext}\shell\{Verb}", throwOnMissingSubKey: false);
            }
            catch { /* best-effort */ }
        }
    }

    /// <summary>Re-apply whatever the settings say (used at startup so the command path stays current
    /// across rebuilds/moves). No-op when packaged.</summary>
    public static void SyncFromSettings(bool openWith, bool contextMenu)
    {
        if (IsPackaged) return;
        SetOpenWith(openWith);
        SetContextMenu(contextMenu);
    }

    // Tell Explorer the file associations changed so the menus refresh without a sign-out.
    private const int SHCNE_ASSOCCHANGED = 0x08000000;
    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
    private static void NotifyShell() => SHChangeNotify(SHCNE_ASSOCCHANGED, 0, IntPtr.Zero, IntPtr.Zero);
}
