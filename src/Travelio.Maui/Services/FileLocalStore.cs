using System.Text.Json;
using Travelio.Application;
namespace Travelio.Maui.Services;

/// <summary>Sandboxed local storage with serialized writes and atomic replacement.</summary>
public sealed class FileLocalStore : ILocalStore
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _directory = Path.Combine(FileSystem.AppDataDirectory, "workspace");
    private string PathFor(string key) => Path.Combine(_directory,
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key))) + ".json");
    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var path = PathFor(key);
            if (!File.Exists(path)) return default;
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<T>(stream, cancellationToken: cancellationToken);
        }
        finally { _gate.Release(); }
    }
    public async Task SetAsync<T>(string key, T value, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(_directory);
            var path = PathFor(key);
            var temporary = path + ".tmp";
            await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            { await JsonSerializer.SerializeAsync(stream, value, cancellationToken: cancellationToken); await stream.FlushAsync(cancellationToken); }
            File.Move(temporary, path, true);
        }
        finally { _gate.Release(); }
    }
    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { File.Delete(PathFor(key)); }
        finally { _gate.Release(); }
    }
}
