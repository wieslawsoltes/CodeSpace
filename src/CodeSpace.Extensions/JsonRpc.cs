using System.Text;
using System.Text.Json;

namespace CodeSpace.Extensions;

/// <summary>Content-Length framing used by LSP and DAP. This transport does not itself implement either protocol's feature set.</summary>
public static class JsonRpcFraming
{
    public static async Task WriteAsync(Stream stream, object message, CancellationToken cancellation = default)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message); var header = Encoding.ASCII.GetBytes($"Content-Length: {bytes.Length}\r\n\r\n");
        await stream.WriteAsync(header, cancellation); await stream.WriteAsync(bytes, cancellation); await stream.FlushAsync(cancellation);
    }
    public static async Task<JsonDocument?> ReadAsync(Stream stream, int maxBytes = 16 * 1024 * 1024, CancellationToken cancellation = default)
    {
        var header = new List<byte>(); var one = new byte[1];
        while (true)
        {
            var count = await stream.ReadAsync(one, cancellation);
            if (count == 0) { if (header.Count == 0) return null; throw new EndOfStreamException("Incomplete RPC header."); }
            header.Add(one[0]); if (header.Count > 8192) throw new InvalidDataException("RPC header is too large.");
            var n = header.Count;
            if (n >= 4 && header[n - 4] == 13 && header[n - 3] == 10 && header[n - 2] == 13 && header[n - 1] == 10) break;
        }
        var lines = Encoding.ASCII.GetString(header.ToArray()).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        int? length = null;
        foreach (var line in lines)
        {
            var colon = line.IndexOf(':'); if (colon < 0) throw new InvalidDataException("Malformed RPC header.");
            if (!line[..colon].Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) continue;
            if (length.HasValue || !int.TryParse(line[(colon + 1)..].Trim(), out var value) || value < 0 || value > maxBytes) throw new InvalidDataException("Invalid Content-Length.");
            length = value;
        }
        if (!length.HasValue) throw new InvalidDataException("Missing Content-Length.");
        var body = new byte[length.Value]; await stream.ReadExactlyAsync(body, cancellation); return JsonDocument.Parse(body);
    }
}

public interface IExtensionBridge : IAsyncDisposable
{
    event EventHandler<string>? MessageReceived;
    bool IsAvailable { get; }
    Task StartAsync(CancellationToken cancellation = default);
    Task SendAsync(string json, CancellationToken cancellation = default);
}
