using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using Shared;
using System.Collections.ObjectModel;
using System.Text.Json;
using Microsoft.Extensions.Options;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Channels;
using System.Runtime.InteropServices;
using System.IO;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.Http.Connections;
using System.Text;
using System.Windows;

namespace Client;

public partial class ClientViewModel : ObservableObject
{
    public ObservableCollection<string> Events { get; } = new();

    [ObservableProperty]
    private string connectionStatus = "Disconnected";

    [ObservableProperty]
    private string? syncDirectory;

    [ObservableProperty]
    private bool isController;

    [RelayCommand]
    private void AddEvent(string message)
    {
        if (App.Current?.Dispatcher != null)
            App.Current.Dispatcher.Invoke(() => Events.Add(message));
        else
            Events.Add(message);
    }

    private readonly FileSyncManager? _fileSync;
    public ClientViewModel() { }
    public ClientViewModel(FileSyncManager fileSync) { _fileSync = fileSync; }

    [RelayCommand]
    private async Task SaveSyncDirectory()
    {
        if (string.IsNullOrWhiteSpace(SyncDirectory)) return;
        _fileSync?.SetDirectory(SyncDirectory!);
        AddEvent($"Sync dir set: {SyncDirectory}");
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task PushInitialSync()
    {
        if (string.IsNullOrWhiteSpace(SyncDirectory) || _fileSync == null) return;
        await _fileSync.PushInitialAsync();
        AddEvent("Initial sync pushed");
    }

    [RelayCommand]
    private async Task RequestControl()
    {
        try
        {
            var baseHttp = ClientWorker.BaseFromHubUrl(ClientWorker.Settings.ServerUrl);
            var target = IsController ? "none" : (ClientWorker.Settings.ClientId ?? Environment.MachineName);
            var url = new Uri(new Uri(baseHttp), $"/api/sync/controller/{target}");
            var http = new HttpClient();
            var resp = await http.PostAsync(url, null);
            AddEvent(IsController ? $"Released control: {(int)resp.StatusCode}" : $"Requested control: {(int)resp.StatusCode}");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Request control failed");
        }
    }
}

public class ClientWorker
{
    private readonly ClientViewModel _vm;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
    internal static ClientSettings Settings = new();
    private HubConnection? _hub;

    public ClientWorker(ClientViewModel vm, IOptions<ClientSettings> options, FileSyncManager fileSync)
    {
        _vm = vm;
        Settings = options.Value;
        fileSync.Attach(this, vm);
        BuildHub();
    }

    public async Task StartAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                if (_hub == null) BuildHub();
                await _hub!.StartAsync(token);
                _vm.ConnectionStatus = "Connected";
                _vm.AddEventCommand.Execute($"Connected to {Settings.ServerUrl}");

                await _hub!.InvokeAsync("Hello", new ClientHello(Settings.ClientId ?? Environment.MachineName, Environment.MachineName, Environment.UserName));

                await SendScreenshotAsync(token);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Client WS error");
                _vm.ConnectionStatus = "Disconnected - retrying";
            }

            await Task.Delay(TimeSpan.FromSeconds(5), token);
        }
    }

    public async ValueTask PublishAsync(Envelope env)
    {
        if (_hub == null) BuildHub();
        if (env.Type == MessageType.MouseEvent && env.Payload is MouseEventMessage mm)
            await _hub!.SendAsync("SendMouseEvent", mm);
        else if (env.Type == MessageType.FileSync && env.Payload is FileSyncMessage fm)
            await _hub!.SendAsync("SendFileSync", fm);
        else
            return;
    }

    private async Task SendScreenshotAsync(CancellationToken token)
    {
        try
        {
            var http = new HttpClient();
            var httpBase = BaseFromHubUrl(Settings.ServerUrl);
            var url = new Uri(new Uri(httpBase), $"/api/clients/{Settings.ClientId ?? Environment.MachineName}/screenshot");
            var upload = new ScreenshotUpload(Settings.ClientId ?? Environment.MachineName, Convert.ToBase64String(Encoding.UTF8.GetBytes("placeholder")), DateTimeOffset.UtcNow);
            var resp = await http.PostAsJsonAsync(url, upload, cancellationToken: token);
            _vm.AddEventCommand.Execute($"Screenshot upload: {(int)resp.StatusCode}");
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to upload screenshot");
        }
    }

    private void HandleEnvelope(Envelope env)
    {
        try
        {
            switch (env.Type)
            {
                case MessageType.RunCommand:
                    _vm.AddEventCommand.Execute("Execute: RunCommand (no return)");
                    break;
                case MessageType.RunService:
                    _vm.AddEventCommand.Execute("Execute: RunService and post result");
                    _ = PostServiceResultAsync(new ServiceResult("ExampleService", Guid.NewGuid().ToString("N"), true, "ok"));
                    break;
                case MessageType.ControllerChanged:
                    {
                        var controller = ((JsonElement)env.Payload).GetProperty("controller").GetString();
                        _vm.IsController = string.Equals(controller, Settings.ClientId ?? Environment.MachineName, StringComparison.OrdinalIgnoreCase);
                        _vm.AddEventCommand.Execute($"Controller = {controller}");
                        break;
                    }
                case MessageType.MouseEvent:
                    _vm.AddEventCommand.Execute("Sync: MouseEvent");
                    ApplyMouseEvent((JsonElement)env.Payload);
                    break;
                case MessageType.FileSync:
                    _vm.AddEventCommand.Execute("Sync: FileSync");
                    ApplyFileSync((JsonElement)env.Payload);
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to handle envelope");
        }
    }

    private async Task PostServiceResultAsync(ServiceResult result)
    {
        if (string.IsNullOrWhiteSpace(Settings.ResultCallback)) return;
        try
        {
            var http = new HttpClient();
            var resp = await http.PostAsJsonAsync(Settings.ResultCallback, result);
            _vm.AddEventCommand.Execute($"ServiceResult posted: {(int)resp.StatusCode}");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to post service result");
        }
    }

    private void ApplyMouseEvent(JsonElement payload)
    {
        try
        {
            var controllerId = payload.GetProperty("controllerClientId").GetString();
            if (string.Equals(controllerId, Settings.ClientId, StringComparison.OrdinalIgnoreCase)) return;
            var action = Enum.Parse<MouseAction>(payload.GetProperty("action").GetString()!, true);
            var nx = payload.GetProperty("normalizedX").GetDouble();
            var ny = payload.GetProperty("normalizedY").GetDouble();
            nx = Math.Clamp(nx, 0, 1);
            ny = Math.Clamp(ny, 0, 1);
            var vx = SystemParameters.VirtualScreenLeft;
            var vy = SystemParameters.VirtualScreenTop;
            var vw = SystemParameters.VirtualScreenWidth;
            var vh = SystemParameters.VirtualScreenHeight;
            var x = (int)(vx + nx * vw);
            var y = (int)(vy + ny * vh);
            var delta = payload.GetProperty("delta").GetInt32();

            switch (action)
            {
                case MouseAction.Move:
                    Native.SetCursorPos(x, y);
                    break;
                case MouseAction.LeftDown:
                    Native.MouseEvent(Native.MOUSEEVENTF_LEFTDOWN, x, y, 0, 0);
                    break;
                case MouseAction.LeftUp:
                    Native.MouseEvent(Native.MOUSEEVENTF_LEFTUP, x, y, 0, 0);
                    break;
                case MouseAction.RightDown:
                    Native.MouseEvent(Native.MOUSEEVENTF_RIGHTDOWN, x, y, 0, 0);
                    break;
                case MouseAction.RightUp:
                    Native.MouseEvent(Native.MOUSEEVENTF_RIGHTUP, x, y, 0, 0);
                    break;
                case MouseAction.Wheel:
                    Native.MouseEvent(Native.MOUSEEVENTF_WHEEL, x, y, delta, 0);
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "ApplyMouseEvent failed");
        }
    }

    private void ApplyFileSync(JsonElement payload)
    {
        try
        {
            var sender = payload.GetProperty("senderClientId").GetString();
            if (string.Equals(sender, Settings.ClientId, StringComparison.OrdinalIgnoreCase)) return;
            var rel = payload.GetProperty("relativePath").GetString()!;
            var op = Enum.Parse<FileSyncOp>(payload.GetProperty("operation").GetString()!, true);
            var content = payload.TryGetProperty("base64Content", out var c) && c.ValueKind != JsonValueKind.Null ? c.GetString() : null;
            var root = ClientWorker.Settings.SyncDirectory;
            if (string.IsNullOrWhiteSpace(root)) return;
            FileSyncApplier.Apply(root!, new FileSyncMessage(sender!, rel, op, content));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "ApplyFileSync failed");
        }
    }

    private void BuildHub()
    {
        var url = Settings.ServerUrl;
        var clientId = Settings.ClientId ?? Environment.MachineName;
        _hub = new HubConnectionBuilder()
            .WithUrl($"{url}?clientId={Uri.EscapeDataString(clientId)}", o =>
            {
                o.Transports = HttpTransportType.WebSockets | HttpTransportType.LongPolling;
                o.SkipNegotiation = false; // ensure negotiate to allow fallback
                o.HttpMessageHandlerFactory = handler =>
                {
                    if (handler is HttpClientHandler h)
                    {
                        h.UseProxy = false; // avoid corporate proxy interference
                    }
                    return handler;
                };
            })
            .WithAutomaticReconnect()
            .Build();
        _hub.On<Envelope>("ReceiveEnvelope", env => HandleEnvelope(env));
        _hub.Reconnecting += error => { _vm.ConnectionStatus = "Reconnecting..."; return Task.CompletedTask; };
        _hub.Reconnected += id => { _vm.ConnectionStatus = "Connected"; return Task.CompletedTask; };
        _hub.Closed += async error => { _vm.ConnectionStatus = "Disconnected"; await Task.Delay(2000); };
    }

    public static string BaseFromHubUrl(string hubUrl)
    {
        var uri = new Uri(hubUrl);
        return uri.GetLeftPart(UriPartial.Authority);
    }
}

public class ClientSettings
{
    public string? ClientId { get; set; }
    public string ServerUrl { get; set; } = "http://localhost:2665/hub";
    public string? ResultCallback { get; set; }
    public string? SyncDirectory { get; set; }
    public bool Controller { get; set; }
}

public static class Native
{
    public const int MOUSEEVENTF_LEFTDOWN = 0x0002;
    public const int MOUSEEVENTF_LEFTUP = 0x0004;
    public const int MOUSEEVENTF_RIGHTDOWN = 0x0008;
    public const int MOUSEEVENTF_RIGHTUP = 0x0010;
    public const int MOUSEEVENTF_WHEEL = 0x0800;

    [DllImport("user32.dll")]
    public static extern bool SetCursorPos(int X, int Y);

    [DllImport("user32.dll")]
    public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

    public static void MouseEvent(uint flags, int x, int y, int data, int extra)
        => mouse_event(flags, (uint)x, (uint)y, (uint)data, UIntPtr.Zero);
}

public class FileSyncManager
{
    private FileSystemWatcher? _watcher;
    private string? _root;
    private ClientWorker? _worker;
    private ClientViewModel? _vm;

    public void Attach(ClientWorker worker, ClientViewModel vm)
    {
        _worker = worker;
        _vm = vm;
        // Prefer local persisted setting
        var persisted = ClientPrefs.Load()?.SyncDirectory;
        if (!string.IsNullOrWhiteSpace(persisted))
        {
            SetDirectory(persisted!);
        }
        else if (!string.IsNullOrWhiteSpace(ClientWorker.Settings.SyncDirectory))
        {
            SetDirectory(ClientWorker.Settings.SyncDirectory!);
        }
    }

    public void SetDirectory(string path)
    {
        Directory.CreateDirectory(path);
        _root = path;
        ClientWorker.Settings.SyncDirectory = path;
        ClientPrefs.Save(new ClientSettings { ClientId = ClientWorker.Settings.ClientId, ServerUrl = ClientWorker.Settings.ServerUrl, ResultCallback = ClientWorker.Settings.ResultCallback, SyncDirectory = path });
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

    private void OnChange(string fullPath, FileSyncOp op)
    {
        try
        {
            if (_worker == null || _root == null) return;
            if (Directory.Exists(fullPath)) return;
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
}

public static class FileSyncApplier
{
    public static void Apply(string root, FileSyncMessage msg)
    {
        var full = Path.Combine(root, msg.RelativePath);
        var dir = Path.GetDirectoryName(full)!;
        Directory.CreateDirectory(dir);
        switch (msg.Operation)
        {
            case FileSyncOp.Create:
            case FileSyncOp.Update:
                if (msg.Base64Content == null) return;
                var bytes = Convert.FromBase64String(msg.Base64Content);
                File.WriteAllBytes(full, bytes);
                break;
            case FileSyncOp.Delete:
                if (File.Exists(full)) File.Delete(full);
                break;
        }
    }
}

public static class ClientPrefs
{
    private static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "cocodex");
    private static string FilePath => Path.Combine(Dir, "clientsettings.json");

    public static void Save(ClientSettings settings)
    {
        Directory.CreateDirectory(Dir);
        var json = System.Text.Json.JsonSerializer.Serialize(settings, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        File.WriteAllText(FilePath, json);
    }

    public static ClientSettings? Load()
    {
        if (!File.Exists(FilePath)) return null;
        var json = File.ReadAllText(FilePath);
        return System.Text.Json.JsonSerializer.Deserialize<ClientSettings>(json, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
    }
}
