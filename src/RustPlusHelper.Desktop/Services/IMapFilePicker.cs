using System.IO;

namespace RustPlusHelper.Desktop.Services;

public interface IMapFilePicker
{
    Task<string?> PickRustMapAsync();
}

public sealed class WindowsMapFilePicker : IMapFilePicker
{
    public Task<string?> PickRustMapAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import Rust world map",
            Filter = "Rust world maps (*.map)|*.map|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
            RestoreDirectory = true
        };

        // Reuse the same Steam-library discovery the map cache uses, rather than assuming the
        // default Program Files install location — Rust is large enough that many players install
        // it on a different drive/library.
        var defaultDirectory = WindowsSteamRustInstallLocator.FindInstallations()
            .Select(installation => Path.Combine(installation, "maps"))
            .FirstOrDefault(Directory.Exists);
        if (defaultDirectory is not null)
        {
            dialog.InitialDirectory = defaultDirectory;
        }

        return Task.FromResult(dialog.ShowDialog() == true ? dialog.FileName : null);
    }
}

public sealed class NullMapFilePicker : IMapFilePicker
{
    public Task<string?> PickRustMapAsync() => Task.FromResult<string?>(null);
}
