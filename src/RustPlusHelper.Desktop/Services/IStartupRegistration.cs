namespace RustPlusHelper.Desktop.Services;

/// <summary>Isolates the per-user "Start with Windows" registry Run-key read/write behind an
/// interface, mirroring <see cref="IMapFilePicker"/> — so tests (bUnit, unit) never touch the real
/// machine's registry.</summary>
public interface IStartupRegistration
{
    bool IsEnabled { get; }

    void SetEnabled(bool enabled);
}

public sealed class WindowsStartupRegistration : IStartupRegistration
{
    private const string RunRegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunRegistryValueName = "RustPlusHelper";

    public bool IsEnabled
    {
        get
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunRegistryKeyPath, writable: false);
            return key?.GetValue(RunRegistryValueName) is string;
        }
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunRegistryKeyPath, writable: true)
            ?? Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunRegistryKeyPath);
        if (enabled)
        {
            var exePath = Environment.ProcessPath ?? System.Reflection.Assembly.GetExecutingAssembly().Location;
            key.SetValue(RunRegistryValueName, $"\"{exePath}\"");
        }
        else
        {
            key.DeleteValue(RunRegistryValueName, throwOnMissingValue: false);
        }
    }
}
