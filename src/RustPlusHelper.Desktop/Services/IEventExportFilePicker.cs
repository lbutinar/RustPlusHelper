namespace RustPlusHelper.Desktop.Services;

public interface IEventExportFilePicker
{
    Task<string?> PickSaveLocationAsync(string suggestedFileName);
}

public sealed class WindowsEventExportFilePicker : IEventExportFilePicker
{
    public Task<string?> PickSaveLocationAsync(string suggestedFileName)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export event history",
            Filter = "CSV file (*.csv)|*.csv",
            FileName = suggestedFileName,
            AddExtension = true,
            DefaultExt = ".csv"
        };

        return Task.FromResult(dialog.ShowDialog() == true ? dialog.FileName : null);
    }
}

public sealed class NullEventExportFilePicker : IEventExportFilePicker
{
    public Task<string?> PickSaveLocationAsync(string suggestedFileName) => Task.FromResult<string?>(null);
}
