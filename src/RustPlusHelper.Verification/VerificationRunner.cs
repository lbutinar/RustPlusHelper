using System.Security.Cryptography;
using RustPlusHelper.Application.Map;
using RustPlusHelper.Application.RustPlus;

namespace RustPlusHelper.Verification;

public sealed class VerificationRunner(IRustPlusClient client)
{
    public async Task<VerificationRunResult> RunAsync(
        RustPlusConnectionOptions connection,
        CancellationToken cancellationToken = default,
        string? cameraCode = null)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var statuses = new Dictionary<string, VerificationRequestStatus>(StringComparer.Ordinal);
        ServerVerificationSummary? serverSummary = null;
        MapVerificationSummary? mapSummary = null;
        TeamVerificationSummary? teamSummary = null;
        ChatVerificationSummary? chatSummary = null;
        MarkerVerificationSummary? markerSummary = null;
        CameraVerificationSummary? cameraSummary = null;
        byte[] mapJpeg = [];
        string? alignmentHtml = null;

        await client.ConnectAsync(connection, cancellationToken).ConfigureAwait(false);

        try
        {
            var server = await client.GetServerInfoAsync(cancellationToken).ConfigureAwait(false);
            statuses["serverInfo"] = Status(server);
            if (server.Data is { } serverData)
            {
                serverSummary = new ServerVerificationSummary(
                    serverData.MapSize,
                    serverData.WipeTimeUtc,
                    serverData.PlayerCount,
                    serverData.MaxPlayerCount,
                    serverData.QueuedPlayerCount);
            }

            var map = await client.GetMapAsync(cancellationToken).ConfigureAwait(false);
            statuses["map"] = Status(map);
            if (map.Data is { } mapData)
            {
                mapJpeg = mapData.JpegImage.ToArray();
                mapSummary = new MapVerificationSummary(
                    mapData.Width,
                    mapData.Height,
                    mapData.OceanMargin,
                    mapData.Monuments.Count,
                    mapJpeg.Length,
                    mapJpeg.Length == 0 ? null : Convert.ToHexString(SHA256.HashData(mapJpeg)));
            }

            var team = await client.GetTeamAsync(cancellationToken).ConfigureAwait(false);
            statuses["teamInfo"] = Status(team);
            if (team.Data is { } teamData)
            {
                teamSummary = new TeamVerificationSummary(
                    teamData.Members.Count,
                    teamData.Members.Count(member => member.IsOnline),
                    teamData.Members.Count(member => member.IsAlive),
                    teamData.Members.Any(member => member.SteamId == teamData.LeaderSteamId));
            }

            var chat = await client.GetTeamChatAsync(cancellationToken).ConfigureAwait(false);
            statuses["teamChat"] = Status(chat);
            if (chat.Data is { } chatData)
            {
                chatSummary = new ChatVerificationSummary(
                    chatData.Messages.Count,
                    chatData.Messages.Count == 0 ? null : chatData.Messages.Min(message => message.SentAtUtc),
                    chatData.Messages.Count == 0 ? null : chatData.Messages.Max(message => message.SentAtUtc));
            }

            var markers = await client.GetMapMarkersAsync(cancellationToken).ConfigureAwait(false);
            statuses["mapMarkers"] = Status(markers);
            if (markers.Data is { } markerData)
            {
                markerSummary = new MarkerVerificationSummary(
                    markerData.Markers.Count,
                    markerData.Markers
                        .GroupBy(marker => marker.Kind.ToString())
                        .OrderBy(group => group.Key, StringComparer.Ordinal)
                        .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal),
                    markerData.Markers
                        .Where(marker => marker.Kind == MapMarkerKind.Unknown && marker.RawType.HasValue)
                        .Select(marker => marker.RawType!.Value)
                        .Distinct()
                        .Order()
                        .ToArray(),
                    markerData.Markers.Sum(marker => marker.VendingOrders?.Count ?? 0));
            }

            if (server.Data is { MapSize: { } mapSize } && map.Data is { Width: { } width, Height: { } height, OceanMargin: { } oceanMargin })
            {
                var alignmentMarkers = MapAlignmentReport.BuildMarkers(mapSize, width, height, oceanMargin, map.Data.Monuments);
                alignmentHtml = MapAlignmentReport.BuildHtml("map.jpg", width, height, alignmentMarkers);
            }

            if (cameraCode is not null)
            {
                var camera = await client.SubscribeToCameraAsync(cameraCode, cancellationToken).ConfigureAwait(false);
                statuses["camera"] = Status(camera);
                cameraSummary = camera.Data is { } cameraData
                    ? new CameraVerificationSummary(
                        cameraCode,
                        cameraData.Width,
                        cameraData.Height,
                        cameraData.IsStaticCamera,
                        cameraData.IsPtzCamera,
                        cameraData.IsAutoTurret,
                        cameraData.IsDrone)
                    : new CameraVerificationSummary(cameraCode, null, null, null, null, null, null);
            }
        }
        finally
        {
            await client.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
        }

        var success = statuses.Values.All(status => status.Success);
        var report = new VerificationReport(
            1,
            DateTimeOffset.UtcNow,
            connection.Server == "fake.invalid" ? "fake" : "live",
            connection.UseFacepunchProxy ? "facepunch-secure-proxy" : "direct-websocket",
            success,
            statuses,
            serverSummary,
            mapSummary,
            teamSummary,
            chatSummary,
            markerSummary,
            cameraSummary);

        return new VerificationRunResult(report, mapJpeg, alignmentHtml);
    }

    private static VerificationRequestStatus Status<T>(RustPlusResult<T> result) => result.IsSuccess
        ? new VerificationRequestStatus(true)
        : new VerificationRequestStatus(false, result.Error?.Code, result.Error?.Message);
}
