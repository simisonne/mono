using Microsoft.Win32;
using System.IO;
using System.Runtime.InteropServices;

namespace mono.Core;

public static class FileIconRegistryService
{
    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int wEventId, int uFlags, IntPtr dwItem1, IntPtr dwItem2);

    public static void RegisterIfNeeded(LibraryDb db)
    {
        try
        {
            if (db.GetSetting("fileIconsRegistered") == "1") return;

            string exeDir = AppContext.BaseDirectory;
            string defaultIcon = Path.Combine(exeDir, "Assets", "Icons", "mono.ico");
            string wavIcon     = Path.Combine(exeDir, "Assets", "Icons", "mono2.ico");

            // Fallback: check if icons are directly in exe dir (single-file publish)
            if (!File.Exists(defaultIcon))
                defaultIcon = Path.Combine(exeDir, "mono.ico");
            if (!File.Exists(wavIcon))
                wavIcon = Path.Combine(exeDir, "mono2.ico");

            // Log paths for debugging
            System.Diagnostics.Debug.WriteLine($"[FileIconRegistry] exeDir: {exeDir}");
            System.Diagnostics.Debug.WriteLine($"[FileIconRegistry] defaultIcon: {defaultIcon}, exists: {File.Exists(defaultIcon)}");
            System.Diagnostics.Debug.WriteLine($"[FileIconRegistry] wavIcon: {wavIcon}, exists: {File.Exists(wavIcon)}");

            SetIcon(".wav",  wavIcon);
            SetIcon(".mp3",  defaultIcon);
            SetIcon(".flac", defaultIcon);

            // Notify Windows Explorer to refresh icons
            SHChangeNotify(0x08000000, 0x0000, IntPtr.Zero, IntPtr.Zero);

            db.SaveSetting("fileIconsRegistered", "1");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FileIconRegistry] Error: {ex.Message}");
            // Don't set the flag so it retries next launch
        }
    }

    private static void SetIcon(string extension, string iconPath)
    {
        if (!File.Exists(iconPath)) return;
        using var key = Registry.CurrentUser.CreateSubKey(
            $@"Software\Classes\{extension}\DefaultIcon", writable: true);
        key?.SetValue("", iconPath);
    }
}
