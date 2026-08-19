using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RustPlusHelper.Application.Vending;

public sealed record ItemDisplay(string ShortName, string Name);

/// <summary>
/// Maps Rust+ numeric item IDs to player-facing names. This is external, versioned reference data
/// (Rust's own item definitions), not something Rust+ itself ever supplies — an unresolved ID must
/// stay visibly a raw ID rather than guessing a name.
/// </summary>
public static class ItemCatalog
{
    public const string CatalogueVersion = "2026.08 (SzyMig/Rust-item-list-JSON, no declared license; id/name facts only)";

    private const string ResourceName = "RustPlusHelper.Application.Vending.rust-items.json";

    private static readonly Lazy<IReadOnlyDictionary<int, ItemDisplay>> Items = new(Load);

    public static ItemDisplay? TryResolve(int itemId) =>
        Items.Value.TryGetValue(itemId, out var item) ? item : null;

    private static IReadOnlyDictionary<int, ItemDisplay> Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded item catalogue resource '{ResourceName}' was not found.");
        var entries = JsonSerializer.Deserialize<CatalogueEntry[]>(stream)
            ?? throw new InvalidOperationException("The embedded item catalogue could not be parsed.");

        var byId = new Dictionary<int, ItemDisplay>(entries.Length);
        foreach (var entry in entries)
        {
            byId[entry.Id] = new ItemDisplay(entry.ShortName, entry.Name);
        }

        return byId;
    }

    private sealed record CatalogueEntry(
        [property: JsonPropertyName("id")] int Id,
        [property: JsonPropertyName("shortName")] string ShortName,
        [property: JsonPropertyName("name")] string Name);
}
