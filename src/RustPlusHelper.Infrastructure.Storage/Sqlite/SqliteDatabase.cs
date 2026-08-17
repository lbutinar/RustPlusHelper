using Microsoft.Data.Sqlite;

namespace RustPlusHelper.Infrastructure.Storage.Sqlite;

public sealed class SqliteDatabase
{
    private readonly object _initializationLock = new();
    private bool _initialized;

    public SqliteDatabase(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        DatabasePath = Path.GetFullPath(databasePath);

        ConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Default,
            ForeignKeys = true,
            DefaultTimeout = 5
        }.ToString();
    }

    public string DatabasePath { get; }

    public string ConnectionString { get; }

    public void Initialize()
    {
        lock (_initializationLock)
        {
            if (_initialized)
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
            using var connection = OpenConnection();
            SqliteMigrationRunner.Apply(connection);
            _initialized = true;
        }
    }

    public SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        return connection;
    }
}
