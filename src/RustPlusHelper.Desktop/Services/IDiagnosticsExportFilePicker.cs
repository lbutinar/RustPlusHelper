namespace RustPlusHelper.Desktop.Services;

public interface IDiagnosticsExportFilePicker
{
    Task<string?> PickSaveLocationAsync(string suggestedFileName);
}

public sealed class WindowsDiagnosticsExportFilePicker : IDiagnosticsExportFilePicker
{
    public Task<string?> PickSaveLocationAsync(string suggestedFileName)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export RustPlusHelper diagnostics",
            Filter = "Zip archive (*.zip)|*.zip",
            FileName = suggestedFileName,
            AddExtension = true,
            DefaultExt = ".zip"
        };

        return Task.FromResult(dialog.ShowDialog() == true ? dialog.FileName : null);
    }
}

public sealed class NullDiagnosticsExportFilePicker : IDiagnosticsExportFilePicker
{
    public Task<string?> PickSaveLocationAsync(string suggestedFileName) => Task.FromResult<string?>(null);
}
