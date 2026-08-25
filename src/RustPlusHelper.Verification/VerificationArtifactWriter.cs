using System.Text.Json;

namespace RustPlusHelper.Verification;

public static class VerificationArtifactWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static async Task<string> WriteAsync(
        string? requestedDirectory,
        VerificationReport report,
        byte[] mapJpeg,
        IReadOnlyCollection<string> forbiddenSecrets,
        CancellationToken cancellationToken = default,
        string? alignmentHtml = null)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(mapJpeg);

        var outputDirectory = Path.GetFullPath(requestedDirectory ?? Path.Combine(
            "artifacts",
            "verification",
            report.CapturedUtc.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture)));

        Directory.CreateDirectory(outputDirectory);

        var json = JsonSerializer.Serialize(report, JsonOptions);
        AssertDoesNotContainSecrets(json, forbiddenSecrets);

        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, "summary.json"),
            json,
            cancellationToken).ConfigureAwait(false);

        if (mapJpeg.Length > 0)
        {
            await File.WriteAllBytesAsync(
                Path.Combine(outputDirectory, "map.jpg"),
                mapJpeg,
                cancellationToken).ConfigureAwait(false);
        }

        if (alignmentHtml is not null)
        {
            await File.WriteAllTextAsync(
                Path.Combine(outputDirectory, "alignment.html"),
                alignmentHtml,
                cancellationToken).ConfigureAwait(false);
        }

        return outputDirectory;
    }

    /// <summary>
    /// Secrets shorter than this are not checked: a short numeric value (e.g. a small PlayerToken)
    /// is near-certain to collide with an unrelated field in the report (a count, a coordinate, a
    /// timestamp digit), which would fail every export without ever indicating an actual leak.
    /// </summary>
    private const int MinimumCheckedSecretLength = 5;

    internal static void AssertDoesNotContainSecrets(string text, IEnumerable<string> forbiddenSecrets)
    {
        foreach (var secret in forbiddenSecrets.Where(
            secret => !string.IsNullOrWhiteSpace(secret) && secret.Length >= MinimumCheckedSecretLength))
        {
            if (text.Contains(secret, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("A verification artifact contained a forbidden credential value.");
            }
        }
    }
}
