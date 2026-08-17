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

        var defaultDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Steam",
            "steamapps",
            "common",
            "Rust",
            "maps");
        if (Directory.Exists(defaultDirectory))
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
