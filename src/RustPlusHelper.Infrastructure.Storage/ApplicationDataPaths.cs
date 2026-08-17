namespace RustPlusHelper.Infrastructure.Storage;

public static class ApplicationDataPaths
{
    public static string GetDatabasePath()
    {
        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localData))
        {
            throw new InvalidOperationException("Windows did not provide a local application-data directory.");
        }

        return Path.Combine(localData, "RustPlusHelper", "rustplushelper.db");
    }
}
