using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Diagnostics;
using System.IO;
using Client;

namespace Client;

public partial class ClientViewModel : ObservableObject
{
    public ObservableCollection<string> Events { get; } = new();

    [ObservableProperty]
    private string connectionStatus = "Disconnected";

    [ObservableProperty]
    private string? syncDirectory;

    // Settings fields shown in the window
    [ObservableProperty]
    private string? clientId;

    [ObservableProperty]
    private string serverUrl = "http://*:2665/hub";

    [ObservableProperty]
    private string? resultCallback;

    [ObservableProperty]
    private double? mouseMoveHz;

    [ObservableProperty]
    private bool startAsController;

    [ObservableProperty]
    private bool overwriteExistingOnly = true;

    [ObservableProperty]
    private bool syncEnabled = false;

    [ObservableProperty]
    private string syncStatusText = "Sync disabled";

    [ObservableProperty]
    private bool isController;

    [ObservableProperty]
    private string? currentControllerId;

    [ObservableProperty]
    private string controlButtonText = "Request Control";

    [ObservableProperty]
    private string controllerStatusText = "No controller";

    [ObservableProperty]
    private bool hasController;

    partial void OnIsControllerChanged(bool value)
    {
        ControlButtonText = value ? "Release Control" : "Request Control";
        UpdateControllerStatus();
    }

    partial void OnCurrentControllerIdChanged(string? value)
    {
        HasController = !string.IsNullOrWhiteSpace(value);
        UpdateControllerStatus();
    }

    private void UpdateControllerStatus()
    {
        if (IsController)
            ControllerStatusText = "You are the controller";
        else if (!string.IsNullOrWhiteSpace(CurrentControllerId))
            ControllerStatusText = $"Controller: {CurrentControllerId}";
        else
            ControllerStatusText = "No controller";
    }

    [RelayCommand]
    public void AddEvent(string message)
    {
        if (App.Current?.Dispatcher != null)
            App.Current.Dispatcher.Invoke(() => Events.Add(message));
        else
            Events.Add(message);
    }

    [RelayCommand]
    private void ClearEvents()
    {
        if (App.Current?.Dispatcher != null)
            App.Current.Dispatcher.Invoke(() => Events.Clear());
        else
            Events.Clear();
    }

    private readonly FileSyncManager? _fileSync;
    public ClientViewModel() { }
    public ClientViewModel(FileSyncManager fileSync) { _fileSync = fileSync; }

    public void LoadPreferencesIfAvailable()
    {
        try
        {
            var prefs = ClientPrefs.Load();
            if (prefs != null)
            {
                ClientId = prefs.ClientId;
                ServerUrl = string.IsNullOrWhiteSpace(prefs.ServerUrl) ? ServerUrl : prefs.ServerUrl;
                ResultCallback = prefs.ResultCallback;
                SyncDirectory = prefs.SyncDirectory;
                StartAsController = prefs.Controller;
                MouseMoveHz = prefs.MouseMoveHz;
                OverwriteExistingOnly = prefs.OverwriteExistingOnly;
                SyncEnabled = prefs.SyncEnabled;
                SyncStatusText = SyncEnabled ? "Sync enabled" : "Sync disabled";
                AddEvent($"Preferences loaded for {ClientId ?? "<unknown>"}");
            }
            else
            {
                // Fall back to current runtime settings if present
                ClientId = ClientWorker.Settings.ClientId;
                ServerUrl = ClientWorker.Settings.ServerUrl ?? ServerUrl;
                ResultCallback = ClientWorker.Settings.ResultCallback;
                SyncDirectory = ClientWorker.Settings.SyncDirectory;
                StartAsController = ClientWorker.Settings.Controller;
                MouseMoveHz = ClientWorker.Settings.MouseMoveHz;
                OverwriteExistingOnly = ClientWorker.Settings.OverwriteExistingOnly;
                SyncEnabled = ClientWorker.Settings.SyncEnabled;
                SyncStatusText = SyncEnabled ? "Sync enabled" : "Sync disabled";
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to load preferences");
        }
    }

    partial void OnOverwriteExistingOnlyChanged(bool value)
    {
        try
        {
            ClientWorker.Settings.OverwriteExistingOnly = value;
            ClientPrefs.Save(new ClientSettings
            {
                ClientId = ClientWorker.Settings.ClientId,
                ServerUrl = ClientWorker.Settings.ServerUrl,
                ResultCallback = ClientWorker.Settings.ResultCallback,
                SyncDirectory = ClientWorker.Settings.SyncDirectory ?? SyncDirectory,
                Controller = ClientWorker.Settings.Controller,
                MouseMoveHz = ClientWorker.Settings.MouseMoveHz,
                OverwriteExistingOnly = value
            });
            AddEvent($"Sync mode: {(value ? "Overwrite existing only" : "Standard apply")}" );
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to persist OverwriteExistingOnly");
        }
    }

    [RelayCommand]
    private async Task SaveSyncDirectory()
    {
        if (string.IsNullOrWhiteSpace(SyncDirectory)) return;
        if (SyncEnabled)
        {
            _fileSync?.SetDirectory(SyncDirectory!);
            AddEvent($"Sync dir set: {SyncDirectory}");
        }
        else
        {
            _fileSync?.SetDirectoryNoWatch(SyncDirectory!);
            AddEvent($"Sync dir saved (disabled): {SyncDirectory}");
        }
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
            var http = new HttpClient();

            // First, check current controller
            var getUrl = new Uri(new Uri(baseHttp), "/api/sync/controller");
            var getResp = await http.GetAsync(getUrl);
            if (getResp.IsSuccessStatusCode)
            {
                var json = await getResp.Content.ReadAsStringAsync();
                // naive parse: look for controller value
                var controllerVal = System.Text.Json.JsonDocument.Parse(json).RootElement.GetProperty("controller").GetString();
                CurrentControllerId = controllerVal;
                if (!string.IsNullOrWhiteSpace(controllerVal) && !string.Equals(controllerVal, ClientWorker.Settings.ClientId, StringComparison.OrdinalIgnoreCase))
                {
                    AddEvent($"Controller in use by {controllerVal}");
                    return;
                }
            }

            // Toggle control state
            var target = IsController ? "none" : (ClientWorker.Settings.ClientId ?? Environment.MachineName);
            var postUrl = new Uri(new Uri(baseHttp), $"/api/sync/controller/{target}");
            var resp = await http.PostAsync(postUrl, null);
            AddEvent(IsController ? $"Released control: {(int)resp.StatusCode}" : $"Requested control: {(int)resp.StatusCode}");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Request control failed");
        }
    }

    [RelayCommand]
    private async Task OpenSyncDirectory()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(SyncDirectory))
            {
                AddEvent("No sync directory set");
                return;
            }
            if (!Directory.Exists(SyncDirectory))
            {
                Directory.CreateDirectory(SyncDirectory);
            }
            var psi = new ProcessStartInfo
            {
                FileName = SyncDirectory,
                UseShellExecute = true
            };
            Process.Start(psi);
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to open sync directory");
        }
    }

    partial void OnSyncEnabledChanged(bool value)
    {
        try
        {
            ClientWorker.Settings.SyncEnabled = value;
            if (value)
            {
                // Start watching if we have a directory
                if (!string.IsNullOrWhiteSpace(SyncDirectory))
                    _fileSync?.SetDirectory(SyncDirectory!);
                else
                    AddEvent("Sync enabled, but no directory set");
                SyncStatusText = "Sync enabled";
            }
            else
            {
                _fileSync?.StopWatching();
                SyncStatusText = "Sync disabled";
            }

            ClientPrefs.Save(new ClientSettings
            {
                ClientId = ClientWorker.Settings.ClientId,
                ServerUrl = ClientWorker.Settings.ServerUrl,
                ResultCallback = ClientWorker.Settings.ResultCallback,
                SyncDirectory = ClientWorker.Settings.SyncDirectory ?? SyncDirectory,
                SyncEnabled = value,
                Controller = ClientWorker.Settings.Controller,
                MouseMoveHz = ClientWorker.Settings.MouseMoveHz,
                OverwriteExistingOnly = ClientWorker.Settings.OverwriteExistingOnly
            });
            AddEvent(value ? "File sync enabled" : "File sync disabled");
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to toggle SyncEnabled");
        }
    }
}
