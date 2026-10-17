using System.IO;

namespace VideoDesk.App;

// WPF retains StreamSource after an OnLoad decode. Release the byte buffer
// when the stream closes, while allowing the frozen bitmap to stay cached.
internal sealed class ImageDecodeStream(byte[] bytes) : Stream
{
    private MemoryStream? source = new(bytes, writable: false);
    private MemoryStream Source => source ?? throw new ObjectDisposedException(nameof(ImageDecodeStream));
    public override bool CanRead => source?.CanRead == true;
    public override bool CanSeek => source?.CanSeek == true;
    public override bool CanWrite => false;
    public override long Length => Source.Length;
    public override long Position { get => Source.Position; set => Source.Position = value; }
    public override int Read(byte[] buffer, int offset, int count) => Source.Read(buffer, offset, count);
    public override int Read(Span<byte> buffer) => Source.Read(buffer);
    public override long Seek(long offset, SeekOrigin origin) => Source.Seek(offset, origin);
    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    protected override void Dispose(bool disposing)
    {
        if (disposing) { source?.Dispose(); source = null; }
        base.Dispose(disposing);
    }
}
