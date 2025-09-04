using Serilog;
using Shared;
using System.Collections.Concurrent;
using System.IO;

namespace Client;

public class FileSyncManager
{
    private FileSystemWatcher? _watcher;
    private string? _root;
    private ClientWorker? _worker;
    private ClientViewModel? _vm;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _suppress = new();

    public void Attach(ClientWorker worker, ClientViewModel vm)
    {
        _worker = worker;
        _vm = vm;
        // Prefer local persisted setting
        var persisted = ClientPrefs.Load()?.SyncDirectory;
        var dir = !string.IsNullOrWhiteSpace(persisted) ? persisted : ClientWorker.Settings.SyncDirectory;
        if (!string.IsNullOrWhiteSpace(dir))
        {
            if (ClientWorker.Settings.SyncEnabled)
                SetDirectory(dir!);
            else
                SetDirectoryNoWatch(dir!);
        }
    }

    public void SetDirectory(string path)
    {
        Directory.CreateDirectory(path);
        _root = path;
        ClientWorker.Settings.SyncDirectory = path;
        ClientPrefs.Save(new ClientSettings { ClientId = ClientWorker.Settings.ClientId, ServerUrl = ClientWorker.Settings.ServerUrl, ResultCallback = ClientWorker.Settings.ResultCallback, SyncDirectory = path, SyncEnabled = ClientWorker.Settings.SyncEnabled, Controller = ClientWorker.Settings.Controller, MouseMoveHz = ClientWorker.Settings.MouseMoveHz, OverwriteExistingOnly = ClientWorker.Settings.OverwriteExistingOnly, SelectedTargets = ClientWorker.Settings.SelectedTargets, FollowControllerMouse = ClientWorker.Settings.FollowControllerMouse });
        if (_vm != null) _vm.SyncDirectory = path;
        _vm?.AddEventCommand.Execute($"Watching {_root}");

        _watcher?.Dispose();
        _watcher = new FileSystemWatcher(_root)
        {
            IncludeSubdirectories = true,
            EnableRaisingEvents = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.DirectoryName
        };
        _watcher.Created += (_, e) => OnChange(e.FullPath, FileSyncOp.Create);
        _watcher.Changed += (_, e) => OnChange(e.FullPath, FileSyncOp.Update);
        _watcher.Deleted += (_, e) => OnDelete(e.FullPath);
        _watcher.Renamed += (_, e) => { OnDelete(e.OldFullPath); OnChange(e.FullPath, FileSyncOp.Create); };
    }

    public void SetDirectoryNoWatch(string path)
    {
        Directory.CreateDirectory(path);
        _root = path;
        ClientWorker.Settings.SyncDirectory = path;
        ClientPrefs.Save(new ClientSettings { ClientId = ClientWorker.Settings.ClientId, ServerUrl = ClientWorker.Settings.ServerUrl, ResultCallback = ClientWorker.Settings.ResultCallback, SyncDirectory = path, SyncEnabled = ClientWorker.Settings.SyncEnabled, Controller = ClientWorker.Settings.Controller, MouseMoveHz = ClientWorker.Settings.MouseMoveHz, OverwriteExistingOnly = ClientWorker.Settings.OverwriteExistingOnly, SelectedTargets = ClientWorker.Settings.SelectedTargets, FollowControllerMouse = ClientWorker.Settings.FollowControllerMouse });
        if (_vm != null) _vm.SyncDirectory = path;
        _vm?.AddEventCommand.Execute($"Sync dir set (watching disabled): {_root}");
        _watcher?.Dispose();
        _watcher = null;
    }

    public void StopWatching()
    {
        try
        {
            _watcher?.Dispose();
            _watcher = null;
            if (_root != null)
                _vm?.AddEventCommand.Execute($"Stopped watching {_root}");
        }
        catch { }
    }

    private void OnChange(string fullPath, FileSyncOp op)
    {
        try
        {
            if (_worker == null || _root == null) return;
            if (Directory.Exists(fullPath)) return;
            var full = Path.GetFullPath(fullPath);
            if (IsSuppressed(full)) return;
            var rel = Path.GetRelativePath(_root, fullPath);
            var bytes = File.ReadAllBytes(fullPath);
            var base64 = Convert.ToBase64String(bytes);
            var msg = new FileSyncMessage(ClientWorker.Settings.ClientId ?? Environment.MachineName, rel, op, base64);
            _worker.PublishAsync(new Envelope(MessageType.FileSync, msg));
            _vm?.AddEventCommand.Execute($"Sent {op} {rel}");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "File change send failed");
        }
    }

    private void OnDelete(string fullPath)
    {
        try
        {
            if (_worker == null || _root == null) return;
            var full = Path.GetFullPath(fullPath);
            if (IsSuppressed(full)) return;
            var rel = Path.GetRelativePath(_root, fullPath);
            var msg = new FileSyncMessage(ClientWorker.Settings.ClientId ?? Environment.MachineName, rel, FileSyncOp.Delete, null);
            _worker.PublishAsync(new Envelope(MessageType.FileSync, msg));
            _vm?.AddEventCommand.Execute($"Sent Delete {rel}");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "File delete send failed");
        }
    }

    public async Task PushInitialAsync()
    {
        if (_root == null || _worker == null) return;
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(_root, file);
            var base64 = Convert.ToBase64String(File.ReadAllBytes(file));
            var msg = new FileSyncMessage(ClientWorker.Settings.ClientId ?? Environment.MachineName, rel, FileSyncOp.Create, base64);
            await _worker.PublishAsync(new Envelope(MessageType.FileSync, msg));
        }
    }

    public void MarkRemoteChange(string relativePath)
    {
        if (_root == null) return;
        var full = Path.GetFullPath(Path.Combine(_root, relativePath));
        _suppress[full] = DateTimeOffset.UtcNow.AddSeconds(3);
    }

    private bool IsSuppressed(string fullPath)
    {
        if (_suppress.TryGetValue(fullPath, out var until))
        {
            if (until > DateTimeOffset.UtcNow) return true;
            _ = _suppress.TryRemove(fullPath, out _);
        }
        return false;
    }
}
