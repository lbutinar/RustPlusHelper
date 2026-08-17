using System.Globalization;
using System.Text.RegularExpressions;

namespace RustPlusHelper.Application.Map;

public sealed record MonumentDisplay(string Name, string Glyph);

/// <summary>Maps Rust prefab tokens to player-facing monument names and compact map glyphs.</summary>
public static partial class MonumentCatalog
{
    private static readonly IReadOnlyDictionary<string, MonumentDisplay> Known =
        new Dictionary<string, MonumentDisplay>(StringComparer.OrdinalIgnoreCase)
        {
            ["AbandonedMilitaryBase"] = new("Abandoned Military Base", "AM"),
            ["airfield_1"] = new("Airfield", "AF"),
            ["airfield_display_name"] = new("Airfield", "AF"),
            ["apartmentcomplex"] = new("Apartment Complex", "AC"),
            ["arctic_research_base_a"] = new("Arctic Research Base", "AR"),
            ["arctic_base_a"] = new("Arctic Research Base", "AR"),
            ["bandit_town"] = new("Bandit Camp", "BC"),
            ["bandit_camp"] = new("Bandit Camp", "BC"),
            ["compound"] = new("Outpost", "OP"),
            ["desert_military_base_a"] = new("Desert Military Base", "MB"),
            ["desert_military_base_b"] = new("Desert Military Base", "MB"),
            ["desert_military_base_c"] = new("Desert Military Base", "MB"),
            ["desert_military_base_d"] = new("Desert Military Base", "MB"),
            ["excavator_1"] = new("Giant Excavator", "GE"),
            ["excavator"] = new("Giant Excavator", "GE"),
            ["ferry_terminal_1"] = new("Ferry Terminal", "FT"),
            ["ferryterminal"] = new("Ferry Terminal", "FT"),
            ["fishing_village_a"] = new("Fishing Village", "FV"),
            ["fishing_village_b"] = new("Fishing Village", "FV"),
            ["fishing_village_c"] = new("Fishing Village", "FV"),
            ["fishing_village_display_name"] = new("Fishing Village", "FV"),
            ["large_fishing_village_display_name"] = new("Large Fishing Village", "LF"),
            ["gas_station_1"] = new("Oxum's Gas Station", "OX"),
            ["gas_station_2"] = new("Oxum's Gas Station", "OX"),
            ["gas_station"] = new("Oxum's Gas Station", "OX"),
            ["harbor_1"] = new("Small Harbor", "SH"),
            ["harbor_2"] = new("Large Harbor", "LH"),
            ["harbor_display_name"] = new("Small Harbor", "SH"),
            ["harbor_2_display_name"] = new("Large Harbor", "LH"),
            ["junkyard_1"] = new("Junkyard", "JY"),
            ["junkyard_display_name"] = new("Junkyard", "JY"),
            ["launch_site_1"] = new("Launch Site", "LS"),
            ["launchsite"] = new("Launch Site", "LS"),
            ["lighthouse"] = new("Lighthouse", "LH"),
            ["lighthouse_display_name"] = new("Lighthouse", "LH"),
            ["military_tunnel_1"] = new("Military Tunnels", "MT"),
            ["military_tunnels_display_name"] = new("Military Tunnels", "MT"),
            ["mining_quarry_a"] = new("Sulfur Quarry", "SU"),
            ["mining_quarry_b"] = new("Stone Quarry", "ST"),
            ["mining_quarry_c"] = new("HQM Quarry", "HQ"),
            ["mining_quarry_sulfur_display_name"] = new("Sulfur Quarry", "SU"),
            ["mining_quarry_stone_display_name"] = new("Stone Quarry", "ST"),
            ["mining_quarry_hqm_display_name"] = new("HQM Quarry", "HQ"),
            ["nuclear_missile_silo"] = new("Missile Silo", "MS"),
            ["missile_silo_monument"] = new("Missile Silo", "MS"),
            ["oilrig_1"] = new("Small Oil Rig", "SR"),
            ["oilrig_2"] = new("Large Oil Rig", "LR"),
            ["oil_rig_small"] = new("Small Oil Rig", "SR"),
            ["large_oil_rig"] = new("Large Oil Rig", "LR"),
            ["outpost"] = new("Outpost", "OP"),
            ["powerplant_1"] = new("Power Plant", "PP"),
            ["power_plant_display_name"] = new("Power Plant", "PP"),
            ["radtown_small_3"] = new("Sewer Branch", "SB"),
            ["sewer_display_name"] = new("Sewer Branch", "SB"),
            ["satellite_dish"] = new("Satellite Dish", "SD"),
            ["satellite_dish_display_name"] = new("Satellite Dish", "SD"),
            ["sphere_tank"] = new("The Dome", "DM"),
            ["dome_monument_name"] = new("The Dome", "DM"),
            ["stables_a"] = new("Ranch", "RA"),
            ["stables_b"] = new("Large Barn", "LB"),
            ["supermarket_1"] = new("Abandoned Supermarket", "SM"),
            ["supermarket_2"] = new("Abandoned Supermarket", "SM"),
            ["supermarket_3"] = new("Abandoned Supermarket", "SM"),
            ["supermarket"] = new("Abandoned Supermarket", "SM"),
            ["trainyard_1"] = new("Train Yard", "TY"),
            ["train_yard_display_name"] = new("Train Yard", "TY"),
            ["train_tunnel_display_name"] = new("Train Tunnel", "TT"),
            ["train_tunnel_link_display_name"] = new("Train Tunnel Link", "TL"),
            ["warehouse"] = new("Mining Outpost", "MO"),
            ["miningoutpost_3"] = new("Mining Outpost", "MO"),
            ["mining_outpost_display_name"] = new("Mining Outpost", "MO"),
            ["water_treatment_plant_1"] = new("Water Treatment Plant", "WT"),
            ["water_treatment_plant_display_name"] = new("Water Treatment Plant", "WT"),
            ["module_900x900_2way_moonpool"] = new("Underwater Lab Entrance", "UE")
        };

    public static MonumentDisplay Resolve(string? tokenOrName)
    {
        var token = NormalizeToken(tokenOrName);
        if (Known.TryGetValue(token, out var known))
        {
            return known;
        }

        if (token.StartsWith("cave_large", StringComparison.OrdinalIgnoreCase))
        {
            return new MonumentDisplay("Large Cave", "LC");
        }

        if (token.StartsWith("cave_medium", StringComparison.OrdinalIgnoreCase))
        {
            return new MonumentDisplay("Medium Cave", "MC");
        }

        if (token.StartsWith("cave_small", StringComparison.OrdinalIgnoreCase))
        {
            return new MonumentDisplay("Small Cave", "SC");
        }

        if (token.StartsWith("ice_lake", StringComparison.OrdinalIgnoreCase))
        {
            return new MonumentDisplay("Ice Lake", "IL");
        }

        if (token.StartsWith("underwater_lab", StringComparison.OrdinalIgnoreCase))
        {
            return new MonumentDisplay("Underwater Lab", "UL");
        }

        if (token.StartsWith("water_well", StringComparison.OrdinalIgnoreCase))
        {
            return new MonumentDisplay("Water Well", "WW");
        }

        if (token.StartsWith("swamp", StringComparison.OrdinalIgnoreCase))
        {
            return new MonumentDisplay("Swamp", "SW");
        }

        var name = Humanize(token);
        return new MonumentDisplay(name, Initials(name));
    }

    private static string NormalizeToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown_monument";
        }

        var token = value.Trim().Replace('\\', '/');
        token = token[(token.LastIndexOf('/') + 1)..];
        if (token.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
        {
            token = token[..^7];
        }

        return token.StartsWith("Monument.", StringComparison.OrdinalIgnoreCase)
            ? token[9..]
            : token;
    }

    private static string Humanize(string token)
    {
        var words = TrailingVariant().Replace(token, string.Empty).Replace('_', ' ').Trim();
        return string.IsNullOrWhiteSpace(words)
            ? "Unknown Monument"
            : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(words.ToLowerInvariant());
    }

    private static string Initials(string name)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(word => !word.Equals("the", StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
        return words.Length switch
        {
            0 => "?",
            1 => words[0][..Math.Min(2, words[0].Length)].ToUpperInvariant(),
            _ => string.Concat(words.Select(word => char.ToUpperInvariant(word[0])))
        };
    }

    [GeneratedRegex("_(?:[0-9]+|[a-z])$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TrailingVariant();
}
