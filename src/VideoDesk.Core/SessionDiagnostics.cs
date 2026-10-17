using System.Diagnostics;

namespace VideoDesk.Core;

// Diagnostic output is opt-in for this process only. User preferences never enable it.
public sealed class SessionDiagnostics(string directory, IEnumerable<string> arguments)
{
    public bool Enabled { get; } = arguments.Any(argument => argument is "--diagnostics" or "--capture-http");

    public Task WriteAsync(string name, Func<string> contents) =>
        Enabled ? PersistAsync(name, contents, append: false) : Task.CompletedTask;

    public Task AppendAsync(string name, Func<string> contents) =>
        Enabled ? PersistAsync(name, contents, append: true) : Task.CompletedTask;

    private async Task PersistAsync(string name, Func<string> contents, bool append)
    {
        try
        {
            var text = contents();
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, name);
            if (append) await File.AppendAllTextAsync(path, text).ConfigureAwait(false);
            else await File.WriteAllTextAsync(path, text).ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine("Diagnostic write failed: " + error.Message);
        }
    }
}
