using System.Globalization;

namespace RustPlusHelper.Verification;

public sealed record VerificationCommandOptions(
    bool UseLiveServer,
    bool AllowInsecureDirect,
    string? OutputDirectory,
    int TimeoutSeconds,
    bool ShowHelp,
    string? CameraCode = null)
{
    public const string Usage = """
        RustPlusHelper Phase 0 verification

        Usage:
          dotnet run --project src/RustPlusHelper.Verification -- --fake [options]
          dotnet run --project src/RustPlusHelper.Verification -- --live [options]

        Options:
          --fake                    Use deterministic fake Rust+ data (default).
          --live                    Connect to the configured live Rust+ server.
          --allow-insecure-direct   Required when RustPlus:UseFacepunchProxy is false.
          --camera <code>           Also subscribe to this camera code and report the raw result
                                    (error code/message included) alongside the other checks.
          --output <directory>      Artifact directory. Defaults under artifacts/verification/.
          --timeout-seconds <n>     Whole-run timeout from 5 to 300 seconds. Default: 60.
          --help                    Show this help.

        Live credentials are read only from .NET user-secrets or environment variables.
        They are never accepted as command-line arguments.
        """;

    public static VerificationCommandOptions Parse(IReadOnlyList<string> args)
    {
        var live = false;
        var fake = false;
        var allowInsecureDirect = false;
        var showHelp = false;
        string? output = null;
        string? cameraCode = null;
        var timeoutSeconds = 60;

        for (var index = 0; index < args.Count; index++)
        {
            switch (args[index])
            {
                case "--live":
                    live = true;
                    break;
                case "--fake":
                    fake = true;
                    break;
                case "--allow-insecure-direct":
                    allowInsecureDirect = true;
                    break;
                case "--help" or "-h":
                    showHelp = true;
                    break;
                case "--output":
                    output = RequireValue(args, ref index, "--output");
                    break;
                case "--camera":
                    cameraCode = RequireValue(args, ref index, "--camera");
                    break;
                case "--timeout-seconds":
                    var text = RequireValue(args, ref index, "--timeout-seconds");
                    if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out timeoutSeconds)
                        || timeoutSeconds is < 5 or > 300)
                    {
                        throw new ArgumentException("--timeout-seconds must be an integer from 5 to 300.");
                    }

                    break;
                default:
                    throw new ArgumentException($"Unknown argument: {args[index]}");
            }
        }

        if (live && fake)
        {
            throw new ArgumentException("Choose either --live or --fake, not both.");
        }

        return new VerificationCommandOptions(live, allowInsecureDirect, output, timeoutSeconds, showHelp, cameraCode);
    }

    private static string RequireValue(IReadOnlyList<string> args, ref int index, string option)
    {
        index++;
        if (index >= args.Count || args[index].StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException($"{option} requires a value.");
        }

        return args[index];
    }
}
