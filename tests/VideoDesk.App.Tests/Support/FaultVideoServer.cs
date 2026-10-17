using System.Net;
using System.IO;
using System.Net.Sockets;
using System.Text;

namespace VideoDesk.App.Tests.Support;

internal enum NetworkFault { None, DelayHeaders, StallBody, DropOnce, Reject }

// An isolated HTTP origin, never a system proxy. The generated Y4M stream is
// 40 seconds of low-resolution video; no downloads or account data are used.
internal sealed class FaultVideoServer : IAsyncDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource lifetime = new();
    private readonly List<Task> connections = [];
    private readonly Task accepting;
    private readonly byte[] video = CreateVideo(), audio = CreateAudio();
    private int fault, requests, dropped, chunkDelay = 50;
    public NetworkFault Fault { get => (NetworkFault)Volatile.Read(ref fault); set => Volatile.Write(ref fault, (int)value); }
    public int Requests => Volatile.Read(ref requests);
    public int DroppedConnections => Volatile.Read(ref dropped);
    public int ChunkDelayMilliseconds { get => Volatile.Read(ref chunkDelay); set => Volatile.Write(ref chunkDelay, value); }
    public string Url { get; }
    public string AudioUrl => Url.Replace("/video.y4m", "/audio.wav", StringComparison.Ordinal);

    public FaultVideoServer()
    {
        listener.Start(); Url = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/video.y4m";
        accepting = Accept();
    }

    private async Task Accept()
    {
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(lifetime.Token);
                lock (connections) connections.Add(Serve(client));
            }
        }
        catch (Exception error) when (error is OperationCanceledException or SocketException) { }
    }

    private async Task Serve(TcpClient client)
    {
        using (client)
        {
            try
            {
                var ct = lifetime.Token; var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, false, 4096, leaveOpen: true);
                var request = await reader.ReadLineAsync(ct) ?? "";
                var content = request.Contains("/audio.wav", StringComparison.Ordinal) ? audio : video;
                long start = 0, end = content.Length - 1; bool range = false;
                for (var size = 0; size < 8192;)
                {
                    var line = await reader.ReadLineAsync(ct);
                    if (string.IsNullOrEmpty(line)) break;
                    size += line.Length;
                    if (line.StartsWith("Range: bytes=", StringComparison.OrdinalIgnoreCase))
                    {
                        var values = line[13..].Split('-');
                        if (long.TryParse(values[0], out var offset)) { start = offset; range = true; }
                        if (values.Length > 1 && long.TryParse(values[1], out var limit)) end = Math.Min(end, limit);
                    }
                }
                Interlocked.Increment(ref requests);
                while (Fault == NetworkFault.DelayHeaders) await Task.Delay(25, ct);
                if (Fault == NetworkFault.Reject || start >= content.Length)
                {
                    await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 503 Service Unavailable\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"), ct); return;
                }
                var header = $"HTTP/1.1 {(range ? "206 Partial Content" : "200 OK")}\r\nContent-Type: video/x-yuv4mpeg\r\nAccept-Ranges: bytes\r\nContent-Length: {end - start + 1}\r\n" +
                    (range ? $"Content-Range: bytes {start}-{end}/{content.Length}\r\n" : "") + "Connection: close\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(header), ct);
                if (request.StartsWith("HEAD ", StringComparison.Ordinal)) return;
                for (var offset = start; offset <= end;)
                {
                    while (Fault == NetworkFault.StallBody) await Task.Delay(25, ct);
                    if (Interlocked.CompareExchange(ref fault, (int)NetworkFault.None, (int)NetworkFault.DropOnce) == (int)NetworkFault.DropOnce) { Interlocked.Increment(ref dropped); return; }
                    var count = (int)Math.Min(4096, end - offset + 1);
                    await stream.WriteAsync(content.AsMemory((int)offset, count), ct); offset += count;
                    await Task.Delay(ChunkDelayMilliseconds, ct);
                }
            }
            catch (Exception error) when (error is IOException or SocketException or OperationCanceledException) { }
        }
    }

    private static byte[] CreateVideo()
    {
        using var output = new MemoryStream();
        output.Write(Encoding.ASCII.GetBytes("YUV4MPEG2 W64 H36 F12:1 Ip A1:1 C420jpeg\n"));
        var frame = new byte[64 * 36 * 3 / 2];
        for (int i = 0; i < 480; i++)
        {
            Array.Fill(frame, (byte)(40 + i % 100), 0, 64 * 36);
            Array.Fill(frame, (byte)128, 64 * 36, frame.Length - 64 * 36);
            output.Write("FRAME\n"u8); output.Write(frame);
        }
        return output.ToArray();
    }

    private static byte[] CreateAudio()
    {
        using var output = new MemoryStream(); using var writer = new BinaryWriter(output);
        const int bytes = 8000 * 2 * 40;
        writer.Write("RIFF"u8); writer.Write(36 + bytes); writer.Write("WAVEfmt "u8);
        writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(8000);
        writer.Write(16000); writer.Write((short)2); writer.Write((short)16); writer.Write("data"u8);
        writer.Write(bytes); writer.Write(new byte[bytes]); return output.ToArray();
    }

    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel(); listener.Stop(); await accepting;
        Task[] pending; lock (connections) pending = connections.ToArray();
        await Task.WhenAll(pending); lifetime.Dispose();
    }
}
