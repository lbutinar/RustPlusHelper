using System.Text.Json;

namespace RustPlusHelper.Infrastructure.Storage.Map;

/// <summary>
/// Shared "deserialize a stored metadata JSON column, wrap any failure as a consistent
/// <see cref="InvalidDataException"/>" helper used identically by <see cref="SqliteMapCacheRepository"/>
/// and <see cref="SqliteMapTopologyRepository"/>.
/// </summary>
internal static class SqliteJsonMetadata
{
    public static T DeserializeOrThrow<T>(string json, JsonSerializerOptions options, string invalidDataMessage)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, options)
                ?? throw new InvalidDataException(invalidDataMessage);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(invalidDataMessage, exception);
        }
    }
}
