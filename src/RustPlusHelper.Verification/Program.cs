using RustPlusHelper.Application.RustPlus;
using RustPlusHelper.Application.Security;
using RustPlusHelper.Application.Testing;
using RustPlusHelper.Infrastructure.RustPlus;

namespace RustPlusHelper.Verification;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        VerificationCommandOptions command;
        try
        {
            command = VerificationCommandOptions.Parse(args);
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            Console.Error.WriteLine(VerificationCommandOptions.Usage);
            return 2;
        }

        if (command.ShowHelp)
        {
            Console.WriteLine(VerificationCommandOptions.Usage);
            return 0;
        }

        RustPlusConnectionOptions connection;
        try
        {
            connection = command.UseLiveServer
                ? LiveConnectionSettingsLoader.Load(command.AllowInsecureDirect)
                : new RustPlusConnectionOptions("fake.invalid", 28082, ulong.MaxValue - 42, 0);
        }
        catch (InvalidOperationException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }

        await using IRustPlusClient client = command.UseLiveServer
            ? new RustPlusApiClient()
            : new FakeRustPlusClient();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(command.TimeoutSeconds));

        try
        {
            Console.WriteLine($"Starting {(command.UseLiveServer ? "live" : "fake")} read-only Rust+ verification.");
            Console.WriteLine($"Transport: {(connection.UseFacepunchProxy ? "Facepunch secure proxy" : "direct ws:// (explicitly allowed)")}");

            var runner = new VerificationRunner(client);
            var result = await runner.RunAsync(connection, timeout.Token, command.CameraCode).ConfigureAwait(false);
            var outputDirectory = await VerificationArtifactWriter.WriteAsync(
                command.OutputDirectory,
                result.Report,
                result.MapJpeg,
                command.UseLiveServer
                    ? [connection.PlayerToken.ToString(System.Globalization.CultureInfo.InvariantCulture)]
                    : [],
                timeout.Token,
                result.AlignmentHtml).ConfigureAwait(false);

            Console.WriteLine($"Verification report: {Path.Combine(outputDirectory, "summary.json")}");
            if (result.MapJpeg.Length > 0)
            {
                Console.WriteLine($"Map image: {Path.Combine(outputDirectory, "map.jpg")}");
            }

            if (result.AlignmentHtml is not null)
            {
                Console.WriteLine($"Map alignment check: {Path.Combine(outputDirectory, "alignment.html")}");
            }

            if (command.CameraCode is not null && result.Report.Requests.TryGetValue("camera", out var cameraStatus))
            {
                Console.WriteLine(cameraStatus.Success
                    ? $"Camera '{command.CameraCode}': subscribed successfully."
                    : $"Camera '{command.CameraCode}': failed — code={cameraStatus.ErrorCode}, message={cameraStatus.ErrorMessage}");
            }

            Console.WriteLine(result.Report.Success
                ? "All requested read-only operations succeeded."
                : "One or more read-only operations failed; inspect the redacted summary.");

            return result.Report.Success ? 0 : 1;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Verification timed out or was cancelled.");
            return 1;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(SecretRedactor.Redact(
                exception.Message,
                connection.PlayerToken.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            return 1;
        }
    }
}
