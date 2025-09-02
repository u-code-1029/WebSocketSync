using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using System.Collections.ObjectModel;
using System.Net.Http;
using Client;

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
}
