using System.Globalization;

namespace RustPlusHelper.Application.RustPlus;

/// <summary>Connection information passed to a Rust+ client.</summary>
public sealed class RustPlusConnectionOptions
{
    public RustPlusConnectionOptions(
        string server,
        int port,
        ulong playerId,
        int playerToken,
        bool useFacepunchProxy = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(server);

        if (port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), port, "Port must be between 1 and 65535.");
        }

        Server = server;
        Port = port;
        PlayerId = playerId;
        PlayerToken = playerToken;
        UseFacepunchProxy = useFacepunchProxy;
    }

    public string Server { get; }

    public int Port { get; }

    public ulong PlayerId { get; }

    /// <summary>A credential. Never write this property to logs, reports, or serialized output.</summary>
    public int PlayerToken { get; }

    public bool UseFacepunchProxy { get; }

    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"Server = {Server}, Port = {Port}, PlayerId = {PlayerId}, PlayerToken = ***, UseFacepunchProxy = {UseFacepunchProxy}");
}
