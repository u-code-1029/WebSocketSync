using Microsoft.AspNetCore.SignalR;
using Serilog;
using Shared;
using System.Collections.Concurrent;

namespace Server;

public class ControlHubState
{
    public string? ControllerClientId { get; set; }
    public ConcurrentDictionary<string, string> ConnectionToClient { get; } = new();
    public object SyncRoot { get; } = new();
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
            lock (_state.SyncRoot)
            {
                var exists = _state.ConnectionToClient.Values.Any(v => string.Equals(v, clientId, StringComparison.OrdinalIgnoreCase));
                if (exists)
                {
                    Log.Warning("Duplicate clientId {ClientId} attempted connection; refusing", clientId);
                    Context.Abort();
                    return Task.CompletedTask;
                }
                _state.ConnectionToClient[Context.ConnectionId] = clientId!;
            }
            Log.Information("Hub connected {Conn} => {Client}", Context.ConnectionId, clientId);
        }
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        if (_state.ConnectionToClient.TryRemove(Context.ConnectionId, out var clientId))
        {
            // If the disconnected client was the controller, clear controller state and notify
            if (string.Equals(clientId, _state.ControllerClientId, StringComparison.OrdinalIgnoreCase))
            {
                _state.ControllerClientId = null;
                _ = Clients.All.SendAsync("ReceiveEnvelope", new Envelope(MessageType.ControllerChanged, new { controller = (string?)null }));
            }
        }
        return base.OnDisconnectedAsync(exception);
    }

    public Task Hello(ClientHello hello)
    {
        // Enforce uniqueness on Hello as well (belt-and-suspenders)
        lock (_state.SyncRoot)
        {
            var exists = _state.ConnectionToClient
                .Any(kvp => kvp.Value.Equals(hello.ClientId, StringComparison.OrdinalIgnoreCase) && kvp.Key != Context.ConnectionId);
            if (exists)
            {
                Log.Warning("Duplicate Hello from {Client}; aborting connection", hello.ClientId);
                Context.Abort();
                return Task.CompletedTask;
            }
            _state.ConnectionToClient[Context.ConnectionId] = hello.ClientId;
        }
        Log.Information("Hello from {Client}", hello.ClientId);
        return Clients.All.SendAsync("ReceiveEnvelope", new Envelope(MessageType.Heartbeat, new { hello = hello.ClientId }));
    }

    public Task BecomeController()
    {
        if (_state.ConnectionToClient.TryGetValue(Context.ConnectionId, out var clientId))
        {
            _state.ControllerClientId = clientId;
            Log.Information("Controller set via hub by {ClientId}", clientId);
            return Clients.All.SendAsync("ReceiveEnvelope", new Envelope(MessageType.ControllerChanged, new { controller = clientId }));
        }
        return Task.CompletedTask;
    }

    public Task SendMouseEvent(MouseEventMessage msg)
    {
        if (!IsController(Context.ConnectionId))
        {
            if (_state.ConnectionToClient.TryGetValue(Context.ConnectionId, out var cid))
            {
                Log.Debug("Drop MouseEvent from non-controller {ClientId}", cid);
            }
            return Task.CompletedTask;
        }
        // Stamp the sender's clientId server-side to ensure correctness
        if (_state.ConnectionToClient.TryGetValue(Context.ConnectionId, out var senderClientId))
        {
            var stamped = msg with { ControllerClientId = senderClientId };
            Log.Verbose("Forward MouseEvent from {ClientId} to others", senderClientId);
            return Clients.Others.SendAsync("ReceiveEnvelope", new Envelope(MessageType.MouseEvent, stamped));
        }
        return Task.CompletedTask;
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
