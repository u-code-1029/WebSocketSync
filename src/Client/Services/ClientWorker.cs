using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.Extensions.Options;
using Serilog;
using Shared;
using System.Text;
using System.Text.Json;
using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;
using System.IO;

namespace Client;

public class ClientWorker
{
    private readonly ClientViewModel _vm;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
    internal static ClientSettings Settings = new();
    private HubConnection? _hub;
    private readonly FileSyncManager _syncManager;

    public ClientWorker(ClientViewModel vm, IOptions<ClientSettings> options, FileSyncManager fileSync)
    {
        _vm = vm;
        Settings = options.Value;
        _syncManager = fileSync;

        // Ensure unique, persisted ClientId if none configured
        var persisted = ClientPrefs.Load();
        if (string.IsNullOrWhiteSpace(Settings.ClientId) && !string.IsNullOrWhiteSpace(persisted?.ClientId))
        {
            Settings.ClientId = persisted!.ClientId;
        }
        if (string.IsNullOrWhiteSpace(Settings.ClientId))
        {
            Settings.ClientId = $"client-{Guid.NewGuid():N}";
            try
            {
                ClientPrefs.Save(new ClientSettings
                {
                    ClientId = Settings.ClientId,
                    ServerUrl = Settings.ServerUrl,
                    ResultCallback = Settings.ResultCallback,
                    SyncDirectory = persisted?.SyncDirectory ?? Settings.SyncDirectory,
                    Controller = Settings.Controller,
                    MouseMoveHz = Settings.MouseMoveHz
                });
                _vm.AddEventCommand.Execute($"Generated ClientId: {Settings.ClientId}");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to persist generated ClientId");
            }
        }

        fileSync.Attach(this, vm);
        BuildHub();
    }

    public async Task StartAsync(CancellationToken token)
    {
        if (_hub == null) BuildHub();

        while (!token.IsCancellationRequested)
        {
            try
            {
                if (_hub == null) BuildHub();

                if (_hub!.State == HubConnectionState.Disconnected)
                {
                    await _hub.StartAsync(token);
                    _vm.ConnectionStatus = "Connected";
                    _vm.AddEventCommand.Execute($"Connected to {Settings.ServerUrl}");

                    await _hub.InvokeAsync("Hello", new ClientHello(Settings.ClientId ?? Environment.MachineName, Environment.MachineName, Environment.UserName));

                    await SendScreenshotAsync(token);

                    await RefreshControllerAsync(token);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Client WS error");
                _vm.ConnectionStatus = "Disconnected - retrying";
                await Task.Delay(TimeSpan.FromSeconds(5), token);
            }

            await Task.Delay(TimeSpan.FromSeconds(1), token);
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
                        _vm.CurrentControllerId = controller;
                        _vm.IsController = string.Equals(controller, Settings.ClientId ?? Environment.MachineName, StringComparison.OrdinalIgnoreCase);
                        _vm.AddEventCommand.Execute($"Controller changed: {controller ?? "<none>"}");
                        break;
                    }
                case MessageType.MouseEvent:
                    {
                        var payload = (JsonElement)env.Payload;
                        try
                        {
                            var controllerId = payload.GetProperty("controllerClientId").GetString();
                            var action = payload.GetProperty("action").GetString();
                            var nx = payload.GetProperty("normalizedX").GetDouble();
                            var ny = payload.GetProperty("normalizedY").GetDouble();
                            var delta = payload.GetProperty("delta").GetInt32();
                            _vm.AddEventCommand.Execute($"Mouse {action} from {controllerId} @ ({nx:F3},{ny:F3}){(action?.Equals("Wheel", StringComparison.OrdinalIgnoreCase) == true ? $" d={delta}" : string.Empty)}");
                        }
                        catch { /* best-effort logging */ }
                        ApplyMouseEvent(payload);
                        break;
                    }
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
            // Do not filter by ClientId here; server already avoids echoing to sender.
            var rel = payload.GetProperty("relativePath").GetString()!;
            FileSyncOp op;
            var opProp = payload.GetProperty("operation");
            if (opProp.ValueKind == JsonValueKind.Number)
                op = (FileSyncOp)opProp.GetInt32();
            else
                op = Enum.Parse<FileSyncOp>(opProp.GetString()!, true);
            var content = payload.TryGetProperty("base64Content", out var c) && c.ValueKind != JsonValueKind.Null ? c.GetString() : null;
            var root = ClientWorker.Settings.SyncDirectory;
            if (string.IsNullOrWhiteSpace(root))
            {
                var defaultRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "cocodex-sync");
                Directory.CreateDirectory(defaultRoot);
                ClientWorker.Settings.SyncDirectory = defaultRoot;
                _vm.AddEventCommand.Execute($"No SyncDirectory configured. Using default: {defaultRoot}");
                root = defaultRoot;
            }
            // Prevent echo loops by suppressing watcher events for this path
            _syncManager.MarkRemoteChange(rel);
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
        _hub.Reconnected += async id =>
        {
            _vm.ConnectionStatus = "Connected";
            try
            {
                await _hub.InvokeAsync("Hello", new ClientHello(Settings.ClientId ?? Environment.MachineName, Environment.MachineName, Environment.UserName));
                await SendScreenshotAsync(CancellationToken.None);
                await RefreshControllerAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Post-reconnect hello/screenshot failed");
            }
        };
        _hub.Closed += async error => { _vm.ConnectionStatus = "Disconnected"; await Task.Delay(2000); };
    }

    public static string BaseFromHubUrl(string hubUrl)
    {
        var uri = new Uri(hubUrl);
        return uri.GetLeftPart(UriPartial.Authority);
    }

    private async Task RefreshControllerAsync(CancellationToken token)
    {
        try
        {
            var http = new HttpClient();
            var httpBase = BaseFromHubUrl(Settings.ServerUrl);
            var url = new Uri(new Uri(httpBase), "/api/sync/controller");
            var resp = await http.GetAsync(url, token);
            if (resp.IsSuccessStatusCode)
            {
                var json = await resp.Content.ReadAsStringAsync(token);
                var controller = JsonDocument.Parse(json).RootElement.GetProperty("controller").GetString();
                _vm.CurrentControllerId = controller;
                _vm.IsController = string.Equals(controller, Settings.ClientId ?? Environment.MachineName, StringComparison.OrdinalIgnoreCase);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to refresh controller state");
        }
    }
}
