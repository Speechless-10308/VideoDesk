namespace VideoDesk.Core.Tests.Support;

internal sealed class TemporaryDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "VideoDesk.Tests", Guid.NewGuid().ToString("N"));

    public TemporaryDirectory() => Directory.CreateDirectory(Path);

    public void Dispose() => Directory.Delete(Path, recursive: true);
}
