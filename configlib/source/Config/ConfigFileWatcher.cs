using System.Diagnostics;
using Vintagestory.API.Common;

namespace ConfigLib;
public sealed class ConfigFileWatcher : IDisposable
{
    private const int _applyPendingChangesIntervalMs = 2000;

    internal ConfigFileWatcher(ICoreAPI api, IConfig config, bool startPaused = false)
    {
        _api = api;
        Config = config;
        
        var fullPath = config.ConfigFilePath;
        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(directory))
        {
            throw new InvalidConfigException($"Invalid config path '{config.ConfigFilePath}' could not find directory");
        }
        
        Watcher = new FileSystemWatcher(directory);
        
        Watcher.Changed += FileEventHandler;
        Watcher.Created += FileEventHandler;
        Watcher.Filter = Path.GetFileName(fullPath);
        Watcher.NotifyFilter = NotifyFilters.CreationTime | NotifyFilters.LastWrite;
        Watcher.Error += (_, e) => Debug.WriteLine(e.GetException());
        IsPaused = startPaused;

        _listenerId = api.World.RegisterGameTickListener(ApplyPendingChanges, _applyPendingChangesIntervalMs, api.World.Rand.Next(100, _applyPendingChangesIntervalMs));
    }
    private readonly object _fileChangedLockObject = new();

    private readonly ICoreAPI _api;
    
    private readonly long _listenerId;
    
    public IConfig Config { get; }

    public bool IsPaused
    {
        get => !Watcher.EnableRaisingEvents;
        set => Watcher.EnableRaisingEvents = !value;
    }

    private bool _hasPendingChanges;

    public FileSystemWatcher Watcher { get; }

    private void FileEventHandler(object sender, FileSystemEventArgs eventArgs)
    {
        if (eventArgs.ChangeType != WatcherChangeTypes.Changed && eventArgs.ChangeType != WatcherChangeTypes.Created) return;

        lock (_fileChangedLockObject)
        {
            _hasPendingChanges = true;
        }
    }

    private void ApplyPendingChanges(float _)
    {
        if(IsPaused) return;
        lock (_fileChangedLockObject)
        {
            if (!_hasPendingChanges) return;
            Config.ReadFromFile();
            _hasPendingChanges = false;
        }
    }

    public void Dispose()
    {
        Watcher.Dispose();
        _api.World.UnregisterGameTickListener(_listenerId);
    }
}
