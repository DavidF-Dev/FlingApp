using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Fling.Config;

public sealed class ConfigStore
{
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(5);

    private readonly string _filePath;
    private readonly string _mutexName;

    public ConfigStore(string filePath)
    {
        _filePath = filePath;
        _mutexName = BuildMutexName(filePath);
    }

    public ConfigStore()
        : this(DefaultPath)
    {
    }

    /// <summary>
    /// Where the shared configuration lives when no path is given.
    /// </summary>
    public static string DefaultPath
    {
        get
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "Fling", "config.json");
        }
    }

    public FlingConfig Load()
    {
        using var guard = AcquireLock();
        return LoadUnlocked();
    }

    public void Save(FlingConfig config)
    {
        using var guard = AcquireLock();
        SaveUnlocked(config);
    }

    /// <summary>
    /// Loads the config, applies <paramref name="mutate"/>, and saves the result without
    /// releasing the lock in between. Callers that read, modify, and write must use this
    /// rather than a Load/Save pair, which another process can interleave with.
    /// </summary>
    public FlingConfig Update(Action<FlingConfig> mutate)
    {
        using var guard = AcquireLock();
        var config = LoadUnlocked();
        mutate(config);
        SaveUnlocked(config);
        return config;
    }

    private FlingConfig LoadUnlocked()
    {
        if (!File.Exists(_filePath))
            return new FlingConfig();

        string json;
        try
        {
            json = File.ReadAllText(_filePath);
        }
        catch (IOException ex)
        {
            throw new ConfigException($"Could not read config file at {_filePath}: {ex.Message}", ex);
        }

        try
        {
            return JsonSerializer.Deserialize(json, ConfigJsonContext.Default.FlingConfig)
                   ?? new FlingConfig();
        }
        catch (JsonException ex)
        {
            throw new ConfigException(
                $"Config file at {_filePath} contains invalid JSON. Fix or delete the file to continue.\n{ex.Message}",
                ex);
        }
    }

    private void SaveUnlocked(FlingConfig config)
    {
        var dir = Path.GetDirectoryName(_filePath)!;
        Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(config, ConfigJsonContext.Default.FlingConfig);

        // Write to a sibling file and swap it in, so a crash or a full disk mid-write
        // cannot leave a truncated file where the API keys used to be.
        var tempPath = _filePath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, _filePath, overwrite: true);
    }

    private MutexGuard AcquireLock() => new(_mutexName);

    /// <summary>
    /// Derives a mutex name from the file path so that stores for different files never
    /// contend, and stores for the same file always do.
    /// </summary>
    private static string BuildMutexName(string filePath)
    {
        var normalized = Path.GetFullPath(filePath).ToLowerInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return $"Local\\Fling.Config.{Convert.ToHexString(hash.AsSpan(0, 16))}";
    }

    /// <summary>
    /// Holds a named mutex for the lifetime of the scope. A caller that times out or
    /// inherits an abandoned mutex proceeds anyway: losing the lock is preferable to
    /// failing the command outright, and the atomic write still prevents corruption.
    /// </summary>
    private sealed class MutexGuard : IDisposable
    {
        private readonly Mutex _mutex;
        private readonly bool _held;

        public MutexGuard(string name)
        {
            _mutex = new Mutex(initiallyOwned: false, name);
            try
            {
                _held = _mutex.WaitOne(LockTimeout);
            }
            catch (AbandonedMutexException)
            {
                _held = true;
            }
        }

        public void Dispose()
        {
            if (_held)
                _mutex.ReleaseMutex();

            _mutex.Dispose();
        }
    }
}
