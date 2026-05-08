using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using GoogleCast;
using GoogleCast.Channels;
using GoogleCast.Models;
using GoogleCast.Models.Media;

namespace Lumyn.Core.Services;

public sealed record ChromecastDevice(string Id, string Name, IReceiver Receiver);

public sealed record ChromecastPositionInfo(TimeSpan Position, TimeSpan Duration);

public sealed record ChromecastMediaMetadata(
    string? Title,
    string? Artist,
        string? Album,
        string? AlbumArtist,
        string? CoverArtPath,
        TimeSpan? Duration,
        bool IsAudio);

public sealed class ChromecastCastService : IDisposable
{
    private const string LumynReceiverApplicationId = "A5A9455D";

    private readonly object _serverLock = new();
    private CancellationTokenSource? _serverCts;
    private TcpListener? _listener;
    private string? _servedFilePath;
    private string? _servedSubtitlePath;
    private string? _servedCoverArtPath;
    private string? _servedSubtitleVttContent;
    private Uri? _servedFileUri;
    private Uri? _servedSubtitleUri;
    private Uri? _servedCoverArtUri;

    private ISender? _sender;
    private IMediaChannel? _mediaChannel;
    private ChromecastDevice? _activeDevice;

    public ChromecastDevice? ActiveDevice => _activeDevice;
    public Uri? ServedFileUri => _servedFileUri;

    public async Task<IReadOnlyList<ChromecastDevice>> DiscoverAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(timeout);
        try
        {
            var receivers = await new DeviceLocator().FindReceiversAsync();
            return receivers
                .Select(r => new ChromecastDevice(r.Id, r.FriendlyName, r))
                .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    public async Task CastAsync(
        ChromecastDevice device,
        string filePath,
        string? subtitlePath,
        TimeSpan startPosition,
        int volume,
        ChromecastMediaMetadata? metadata = null,
        CancellationToken cancellationToken = default)
    {
        await DisconnectSenderAsync();

        var (mediaUri, subtitleUri, coverArtUri) = StartServer(filePath, subtitlePath, metadata?.CoverArtPath);

        var sender = new Sender();
        sender.Disconnected += OnDisconnected;
        _sender = sender;

        await sender.ConnectAsync(device.Receiver);

        // Stop any currently running Cast app on the receiver before launching a new
        // session. This guarantees a clean slate so subtitle tracks from a previous
        // cast do not linger in the session state on the TV.
        try
        {
            var receiverChannel = sender.GetChannel<IReceiverChannel>();
            await receiverChannel.StopAsync();
        }
        catch { /* no app running yet, or stop not supported — safe to ignore */ }

        var mediaChannel = sender.GetChannel<IMediaChannel>();
        ConfigureReceiverApplicationId(mediaChannel);
        await sender.LaunchAsync(mediaChannel);
        _mediaChannel = mediaChannel;

        try
        {
            var receiverChannel = sender.GetChannel<IReceiverChannel>();
            await receiverChannel.SetVolumeAsync(Math.Clamp(volume, 0, 100) / 100f);
        }
        catch { /* volume is optional */ }

        Track[]? tracks = null;
        int[] activeTrackIds = [];
        if (subtitleUri is not null)
        {
            tracks =
            [
                new Track
                {
                    TrackId = 1,
                    Type = TrackType.Text,
                    TrackContentId = subtitleUri.ToString(),
                    TrackContentType = "text/vtt",
                    SubType = TextTrackType.Subtitles,
                    Name = "Subtitle"
                }
            ];
            activeTrackIds = [1];
        }

        var mediaInfo = new MediaInformation
        {
            ContentId = mediaUri.ToString(),
            ContentType = GetMimeType(filePath),
            StreamType = StreamType.Buffered,
            Metadata = BuildCastMetadata(filePath, metadata, coverArtUri),
            Duration = metadata?.Duration is { TotalSeconds: > 0 } duration ? duration.TotalSeconds : null,
            Tracks = tracks
        };

        await mediaChannel.LoadAsync(mediaInfo, true, activeTrackIds);

        if (startPosition > TimeSpan.Zero)
        {
            try { await mediaChannel.SeekAsync(startPosition.TotalSeconds); }
            catch { /* not all receivers support pre-seek */ }
        }

        _activeDevice = device;
    }

    public async Task PlayAsync(CancellationToken cancellationToken = default)
    {
        if (_mediaChannel is null) return;
        try { await _mediaChannel.PlayAsync(); } catch { }
    }

    public async Task PauseAsync(CancellationToken cancellationToken = default)
    {
        if (_mediaChannel is null) return;
        try { await _mediaChannel.PauseAsync(); } catch { }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_mediaChannel is null) return;
        try { await _mediaChannel.StopAsync(); } catch { }
    }

    public async Task SetVolumeAsync(int volume, CancellationToken cancellationToken = default)
    {
        if (_sender is null) return;
        try
        {
            var receiverChannel = _sender.GetChannel<IReceiverChannel>();
            await receiverChannel.SetVolumeAsync(Math.Clamp(volume, 0, 100) / 100f);
        }
        catch { }
    }

    public async Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default)
    {
        if (_mediaChannel is null) return;
        try { await _mediaChannel.SeekAsync(position.TotalSeconds); } catch { }
    }

    public async Task<ChromecastPositionInfo?> GetPositionInfoAsync(CancellationToken cancellationToken = default)
    {
        if (_mediaChannel is null) return null;
        try
        {
            var status = await _mediaChannel.GetStatusAsync();
            if (status is null) return null;
            var position = TimeSpan.FromSeconds(status.CurrentTime);
            var duration = status.Media?.Duration is double d and > 0
                ? TimeSpan.FromSeconds(d)
                : TimeSpan.Zero;
            return new ChromecastPositionInfo(position, duration);
        }
        catch { return null; }
    }

    public async Task<bool?> GetIsPlayingAsync(CancellationToken cancellationToken = default)
    {
        if (_mediaChannel is null) return null;
        try
        {
            var status = await _mediaChannel.GetStatusAsync();
            var state = status?.PlayerState;
            if (state is null) return null;
            return state is "PLAYING" or "BUFFERING";
        }
        catch { return null; }
    }

    public void StopServer()
    {
        lock (_serverLock)
        {
            _activeDevice = null;
            StopServerInternal();
        }
        _ = DisconnectSenderAsync();
    }

    public void Dispose() => StopServer();

    private void OnDisconnected(object? sender, EventArgs e)
    {
        _activeDevice = null;
        _mediaChannel = null;
    }

    private async Task DisconnectSenderAsync()
    {
        var sender = _sender;
        _sender = null;
        _mediaChannel = null;
        if (sender is null) return;
        sender.Disconnected -= OnDisconnected;
        try { sender.Disconnect(); } catch { }
        if (sender is IDisposable d) d.Dispose();
        await Task.CompletedTask;
    }

    // ── HTTP file server ──────────────────────────────────────────────────────

    private static void ConfigureReceiverApplicationId(IMediaChannel mediaChannel)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var applicationIdField = mediaChannel.GetType().GetField("<ApplicationId>k__BackingField", flags);
        applicationIdField?.SetValue(mediaChannel, LumynReceiverApplicationId);
    }

    private (Uri mediaUri, Uri? subtitleUri, Uri? coverArtUri) StartServer(string filePath, string? subtitlePath, string? coverArtPath)
    {
        lock (_serverLock)
        {
            var normalizedSubtitle = !string.IsNullOrWhiteSpace(subtitlePath) && File.Exists(subtitlePath)
                ? subtitlePath
                : null;
            var normalizedCoverArt = !string.IsNullOrWhiteSpace(coverArtPath) && File.Exists(coverArtPath)
                ? coverArtPath
                : null;

            if (_listener is not null &&
                string.Equals(_servedFilePath, filePath, StringComparison.Ordinal) &&
                string.Equals(_servedSubtitlePath, normalizedSubtitle, StringComparison.Ordinal) &&
                string.Equals(_servedCoverArtPath, normalizedCoverArt, StringComparison.Ordinal))
                return (_servedFileUri!, _servedSubtitleUri, _servedCoverArtUri);

            StopServerInternal();

            var address = GetLanAddress() ?? IPAddress.Loopback;
            var port = GetFreePort();
            var mediaUri = new Uri($"http://{address}:{port}/lumyn-cast/media/{Uri.EscapeDataString(Path.GetFileName(filePath))}");

            Uri? subtitleUri = null;
            string? vttContent = null;
            if (normalizedSubtitle is not null)
            {
                vttContent = ConvertSubtitleToVtt(normalizedSubtitle);
                subtitleUri = new Uri($"http://{address}:{port}/lumyn-cast/subtitle/{Uri.EscapeDataString(Path.GetFileNameWithoutExtension(normalizedSubtitle))}.vtt");
            }

            Uri? coverArtUri = null;
            if (normalizedCoverArt is not null)
                coverArtUri = new Uri($"http://{address}:{port}/lumyn-cast/art/{Uri.EscapeDataString(Path.GetFileName(normalizedCoverArt))}");

            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();
            _serverCts = new CancellationTokenSource();
            _servedFilePath = filePath;
            _servedSubtitlePath = normalizedSubtitle;
            _servedCoverArtPath = normalizedCoverArt;
            _servedSubtitleVttContent = vttContent;
            _servedFileUri = mediaUri;
            _servedSubtitleUri = subtitleUri;
            _servedCoverArtUri = coverArtUri;
            _ = Task.Run(() => ServerLoopAsync(_listener, _serverCts.Token));
            return (mediaUri, subtitleUri, coverArtUri);
        }
    }

    private void StopServerInternal()
    {
        _servedFilePath = null;
        _servedSubtitlePath = null;
        _servedCoverArtPath = null;
        _servedSubtitleVttContent = null;
        _servedFileUri = null;
        _servedSubtitleUri = null;
        _servedCoverArtUri = null;
        _serverCts?.Cancel();
        _listener?.Stop();
        _listener = null;
        _serverCts?.Dispose();
        _serverCts = null;
    }

    private async Task ServerLoopAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                _ = Task.Run(() => ServeClientAsync(client, cancellationToken), cancellationToken);
            }
            catch (OperationCanceledException) { break; }
            catch
            {
                if (!cancellationToken.IsCancellationRequested)
                    await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task ServeClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using var _ = client;
        using var network = client.GetStream();

        var request = await ReadHttpRequestAsync(network, cancellationToken).ConfigureAwait(false);
        if (request is null) return;

        var isSubtitle = request.Value.Path.Contains("/subtitle/", StringComparison.OrdinalIgnoreCase);
        if (isSubtitle)
        {
            var vtt = _servedSubtitleVttContent;
            if (vtt is null)
            {
                await WriteStatusAsync(network, 404, "Not Found", cancellationToken).ConfigureAwait(false);
                return;
            }
            var bytes = Encoding.UTF8.GetBytes(vtt);
            await WriteResponseHeadersAsync(network, 200, "OK", "text/vtt; charset=utf-8", bytes.Length, null, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(request.Value.Method, "HEAD", StringComparison.OrdinalIgnoreCase))
                await network.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            return;
        }

        var isCoverArt = request.Value.Path.Contains("/art/", StringComparison.OrdinalIgnoreCase);
        if (isCoverArt)
        {
            var coverArtPath = _servedCoverArtPath;
            if (string.IsNullOrWhiteSpace(coverArtPath) || !File.Exists(coverArtPath))
            {
                await WriteStatusAsync(network, 404, "Not Found", cancellationToken).ConfigureAwait(false);
                return;
            }

            try
            {
                var bytes = await File.ReadAllBytesAsync(coverArtPath, cancellationToken).ConfigureAwait(false);
                await WriteResponseHeadersAsync(network, 200, "OK", GetImageMimeType(coverArtPath), bytes.Length, null, cancellationToken).ConfigureAwait(false);
                if (!string.Equals(request.Value.Method, "HEAD", StringComparison.OrdinalIgnoreCase))
                    await network.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            }
            catch { /* client disconnected early or artwork could not be read */ }
            return;
        }

        var path = _servedFilePath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            await WriteStatusAsync(network, 404, "Not Found", cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            var info = new FileInfo(path);
            var start = 0L;
            var end = info.Length - 1;
            request.Value.Headers.TryGetValue("range", out var range);
            var statusCode = 200;
            string? contentRange = null;
            if (!string.IsNullOrWhiteSpace(range) && range.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
            {
                var parts = range["bytes=".Length..].Split('-', 2);
                if (long.TryParse(parts[0], out var parsedStart))
                    start = Math.Clamp(parsedStart, 0, end);
                if (parts.Length > 1 && long.TryParse(parts[1], out var parsedEnd))
                    end = Math.Clamp(parsedEnd, start, info.Length - 1);
                statusCode = 206;
                contentRange = $"bytes {start}-{end}/{info.Length}";
            }

            var length = end - start + 1;
            await WriteResponseHeadersAsync(network, statusCode, statusCode == 206 ? "Partial Content" : "OK",
                GetMimeType(path), length, contentRange, cancellationToken).ConfigureAwait(false);

            if (string.Equals(request.Value.Method, "HEAD", StringComparison.OrdinalIgnoreCase))
                return;

            await using var stream = File.OpenRead(path);
            stream.Seek(start, SeekOrigin.Begin);
            var buffer = new byte[128 * 1024];
            var remaining = length;
            while (remaining > 0 && !cancellationToken.IsCancellationRequested)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), cancellationToken).ConfigureAwait(false);
                if (read == 0) break;
                await network.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                remaining -= read;
            }
        }
        catch { /* client disconnected early — normal for range/probe requests */ }
    }

    /// <summary>
    /// Returns <c>true</c> for container formats the Chromecast cannot play natively.
    /// Used by the UI to show an "unsupported format" warning before attempting a cast.
    /// </summary>
    public static bool IsUnsupportedFormat(string path)
        => Path.GetExtension(path).ToLowerInvariant() is ".mkv" or ".avi" or ".wmv" or ".mov" or ".flv" or ".ts" or ".m2ts";

    private static async Task<(string Method, string Path, Dictionary<string, string> Headers)?> ReadHttpRequestAsync(
        NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), cancellationToken).ConfigureAwait(false);
            if (read == 0) return null;
            total += read;
            var text = Encoding.ASCII.GetString(buffer, 0, total);
            var end = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (end < 0) continue;

            var lines = text[..end].Split("\r\n", StringSplitOptions.None);
            var requestLine = lines.FirstOrDefault()?.Split(' ', 3);
            if (requestLine is null || requestLine.Length < 2) return null;

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in lines.Skip(1))
            {
                var idx = line.IndexOf(':');
                if (idx <= 0) continue;
                headers[line[..idx].Trim()] = line[(idx + 1)..].Trim();
            }

            return (requestLine[0], requestLine[1], headers);
        }
        return null;
    }

    private static async Task WriteResponseHeadersAsync(
        NetworkStream stream, int status, string reason, string contentType,
        long contentLength, string? contentRange, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder()
            .Append(CultureInfo.InvariantCulture, $"HTTP/1.1 {status} {reason}\r\n")
            .Append(CultureInfo.InvariantCulture, $"Content-Type: {contentType}\r\n")
            .Append(CultureInfo.InvariantCulture, $"Content-Length: {contentLength}\r\n")
            .Append("Accept-Ranges: bytes\r\n")
            .Append("Access-Control-Allow-Origin: *\r\n")
            .Append("Connection: close\r\n");
        if (!string.IsNullOrWhiteSpace(contentRange))
            builder.Append(CultureInfo.InvariantCulture, $"Content-Range: {contentRange}\r\n");
        builder.Append("\r\n");

        await stream.WriteAsync(Encoding.ASCII.GetBytes(builder.ToString()), cancellationToken).ConfigureAwait(false);
    }

    private static Task WriteStatusAsync(NetworkStream stream, int status, string reason, CancellationToken cancellationToken)
        => WriteResponseHeadersAsync(stream, status, reason, "text/plain", 0, null, cancellationToken);

    private static GenericMediaMetadata BuildCastMetadata(string filePath, ChromecastMediaMetadata? metadata, Uri? coverArtUri)
    {
        var title = FirstNonBlank(metadata?.Title, Path.GetFileNameWithoutExtension(filePath));
        var subtitle = BuildMetadataSubtitle(metadata);

        var castMetadata = new GenericMediaMetadata
        {
            Title = title,
            Subtitle = subtitle,
            Images = coverArtUri is null
                ? null
                :
                [
                    new Image
                    {
                        Url = coverArtUri.ToString()
                    }
                ]
        };
        SetMetadataType(castMetadata, metadata?.IsAudio == true ? MetadataType.Music : MetadataType.Movie);
        return castMetadata;
    }

    private static void SetMetadataType(GenericMediaMetadata metadata, MetadataType metadataType)
    {
        try
        {
            typeof(GenericMediaMetadata)
                .GetProperty(nameof(GenericMediaMetadata.MetadataType))
                ?.SetValue(metadata, metadataType);
        }
        catch
        {
            // Older GoogleCast metadata models still display title/subtitle/images
            // even when the protected metadata type cannot be changed.
        }
    }

    private static string? BuildMetadataSubtitle(ChromecastMediaMetadata? metadata)
    {
        if (metadata is null) return null;

        var people = FirstNonBlank(metadata.Artist, metadata.AlbumArtist);
        var album = FirstNonBlank(metadata.Album);

        if (!string.IsNullOrWhiteSpace(people) && !string.IsNullOrWhiteSpace(album))
            return $"{people} - {album}";
        return FirstNonBlank(people, album);
    }

    private static string? FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();

    // ── SRT → WebVTT conversion ───────────────────────────────────────────────

    private static string ConvertSubtitleToVtt(string subtitlePath)
    {
        var ext = Path.GetExtension(subtitlePath).ToLowerInvariant();
        var content = File.ReadAllText(subtitlePath, Encoding.UTF8);
        if (ext == ".vtt")
            return content.StartsWith("WEBVTT", StringComparison.Ordinal) ? content : "WEBVTT\n\n" + content;
        if (ext is ".srt" or ".sub")
            return SrtToVtt(content);
        // ASS/SSA and others: Chromecast will reject, but serve anyway so the error is on the device side
        return content;
    }

    private static string SrtToVtt(string srt)
    {
        var sb = new StringBuilder("WEBVTT\n\n");
        foreach (var line in srt.ReplaceLineEndings("\n").Split('\n'))
        {
            // Skip cue index lines (digit-only) and blank lines between cues
            if (string.IsNullOrWhiteSpace(line)) { sb.AppendLine(); continue; }
            if (line.Trim().All(char.IsAsciiDigit)) continue;

            // Timestamp lines: SRT uses comma, VTT uses dot for milliseconds
            sb.AppendLine(line.Contains("-->") ? line.Replace(',', '.') : line);
        }
        return sb.ToString();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string GetMimeType(string path)
        => Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".mp4" or ".m4v" => "video/mp4",
            ".mkv"           => "video/x-matroska",
            ".webm"          => "video/webm",
            ".avi"           => "video/x-msvideo",
            ".mov"           => "video/quicktime",
            ".mp3"           => "audio/mpeg",
            ".flac"          => "audio/flac",
            ".ogg" or ".oga" => "audio/ogg",
            ".opus"          => "audio/opus",
            ".wav"           => "audio/wav",
            ".m4a" or ".aac" => "audio/aac",
            _                => "application/octet-stream"
        };

    private static string GetImageMimeType(string path)
        => Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png"            => "image/png",
            ".webp"           => "image/webp",
            _                 => "application/octet-stream"
        };

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static IPAddress? GetLanAddress()
    {
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up ||
                ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                continue;
            foreach (var unicast in ni.GetIPProperties().UnicastAddresses)
            {
                if (unicast.Address.AddressFamily == AddressFamily.InterNetwork &&
                    !IPAddress.IsLoopback(unicast.Address))
                    return unicast.Address;
            }
        }
        return null;
    }
}
