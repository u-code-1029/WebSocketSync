using Microsoft.AspNetCore.SignalR;
using Serilog;
using Shared;
using System.Collections.Concurrent;

namespace Server;

public class ControlHubState
{
    public string? ControllerClientId { get; set; }
    public ConcurrentDictionary<string,string> ConnectionToClient { get; } = new();
}

public class ControlHub : Hub
{
    private readonly ControlHubState _state;

    public ControlHub(ControlHubState state)
    {
        _state = state;
    }

    public override Task OnConnectedAsync()
    {
        var clientId = Context.GetHttpContext()?.Request.Query["clientId"].ToString();
        if (!string.IsNullOrWhiteSpace(clientId))
        {
            _state.ConnectionToClient[Context.ConnectionId] = clientId!;
            Log.Information("Hub connected {Conn} => {Client}", Context.ConnectionId, clientId);
        }
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _state.ConnectionToClient.TryRemove(Context.ConnectionId, out _);
        return base.OnDisconnectedAsync(exception);
    }

    public Task Hello(ClientHello hello)
    {
        _state.ConnectionToClient[Context.ConnectionId] = hello.ClientId;
        Log.Information("Hello from {Client}", hello.ClientId);
        return Clients.All.SendAsync("ReceiveEnvelope", new Envelope(MessageType.Heartbeat, new { hello = hello.ClientId }));
    }

    public Task SendMouseEvent(MouseEventMessage msg)
    {
        if (!IsController(Context.ConnectionId)) return Task.CompletedTask;
        return Clients.Others.SendAsync("ReceiveEnvelope", new Envelope(MessageType.MouseEvent, msg));
    }

    public Task SendFileSync(FileSyncMessage msg)
    {
        return Clients.Others.SendAsync("ReceiveEnvelope", new Envelope(MessageType.FileSync, msg));
    }

    private bool IsController(string connectionId)
    {
        if (!_state.ConnectionToClient.TryGetValue(connectionId, out var clientId)) return false;
        return string.Equals(clientId, _state.ControllerClientId, StringComparison.OrdinalIgnoreCase);
    }
}

