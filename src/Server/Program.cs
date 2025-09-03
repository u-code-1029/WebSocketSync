using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Serilog;
using Shared;
using System.Text.Json;
using Server;
using System.Linq;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

builder.Services.AddSignalR(o =>
{
    // Allow large messages (e.g., images) up to 100 MB
    o.MaximumReceiveMessageSize = 100 * 1024 * 1024; // 100 MB
});
builder.Services.AddSingleton<ControlHubState>();
builder.Services.AddControllers();

var app = builder.Build();

app.UseSerilogRequestLogging();
// Using SignalR for real-time messaging; also configure WebSocket buffers
app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(30),
    ReceiveBufferSize = 4 * 1024 * 1024 // 4 MB frame buffer
});

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// SignalR hub endpoint
app.MapHub<ControlHub>("/hub");

// HTTP API: Task A.1 Run command (broadcast to clients). Validate first.
app.MapPost("/api/commands/run-cmd", async (RunCommandRequest req, string? targetClientId, IHubContext<ControlHub> hub, ControlHubState state) =>
{
    try
    {
        if (!ValidateUserCommand(req))
        {
            return Results.BadRequest(new { error = "Command rejected" });
        }

        if (!string.IsNullOrWhiteSpace(targetClientId))
        {
            var targets = state.ConnectionToClient
                .Where(kvp => string.Equals(kvp.Value, targetClientId, StringComparison.OrdinalIgnoreCase))
                .Select(kvp => kvp.Key)
                .ToList();

            if (targets.Count == 0)
            {
                Log.Warning("Target client {ClientId} not connected for RunCommand", targetClientId);
                return Results.NotFound(new { error = "Client not connected", clientId = targetClientId });
            }

            await hub.Clients.Clients(targets).SendAsync("ReceiveEnvelope", new Envelope(MessageType.RunCommand, req));
            Log.Information("Sent RunCommand to {ClientId}: {Command} {Arguments}", targetClientId, req.Command, req.Arguments);
        }
        else
        {
            await hub.Clients.All.SendAsync("ReceiveEnvelope", new Envelope(MessageType.RunCommand, req));
            Log.Information("Broadcasted RunCommand: {Command} {Arguments}", req.Command, req.Arguments);
        }

        return Results.Accepted("/api/commands/run-cmd");
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Error in /api/commands/run-cmd for target {TargetClientId}", targetClientId);
        return Results.Problem("An unexpected error occurred.");
    }
});

// HTTP API: Task A.2 Trigger a service.run and expect result to come back later.
app.MapPost("/api/tasks/service-run", async (RunServiceRequest req, string? targetClientId, IHubContext<ControlHub> hub, ControlHubState state) =>
{
    try
    {
        if (!string.IsNullOrWhiteSpace(targetClientId))
        {
            var targets = state.ConnectionToClient
                .Where(kvp => string.Equals(kvp.Value, targetClientId, StringComparison.OrdinalIgnoreCase))
                .Select(kvp => kvp.Key)
                .ToList();

            if (targets.Count == 0)
            {
                Log.Warning("Target client {ClientId} not connected for RunService", targetClientId);
                return Results.NotFound(new { error = "Client not connected", clientId = targetClientId });
            }

            await hub.Clients.Clients(targets).SendAsync("ReceiveEnvelope", new Envelope(MessageType.RunService, req));
            Log.Information("Sent RunService to {ClientId} {Service} Correlation {CorrelationId}", targetClientId, req.ServiceName, req.CorrelationId);
        }
        else
        {
            await hub.Clients.All.SendAsync("ReceiveEnvelope", new Envelope(MessageType.RunService, req));
            Log.Information("Broadcasted RunService {Service} Correlation {CorrelationId}", req.ServiceName, req.CorrelationId);
        }

        return Results.Accepted($"/api/tasks/service-run/{req.CorrelationId}");
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Error in /api/tasks/service-run for target {TargetClientId} Correlation {CorrelationId}", targetClientId, req.CorrelationId);
        return Results.Problem("An unexpected error occurred.");
    }
});

// Endpoint for clients to POST their service result back (optional; for auditing)
app.MapPost("/api/clients/service-result", async (ServiceResult result, string? targetClientId, IHubContext<ControlHub> hub, ControlHubState state) =>
{
    try
    {
        Log.Information("ServiceResult {Service} Correlation {CorrelationId} Success {Success} Message {Message}",
            result.ServiceName, result.CorrelationId, result.Success, result.Message);

        if (!string.IsNullOrWhiteSpace(targetClientId))
        {
            var targets = state.ConnectionToClient
                .Where(kvp => string.Equals(kvp.Value, targetClientId, StringComparison.OrdinalIgnoreCase))
                .Select(kvp => kvp.Key)
                .ToList();

            if (targets.Count == 0)
            {
                Log.Warning("Target client {ClientId} not connected for ServiceResult", targetClientId);
                return Results.NotFound(new { error = "Client not connected", clientId = targetClientId });
            }

            await hub.Clients.Clients(targets).SendAsync("ReceiveEnvelope", new Envelope(MessageType.ServiceResult, result));
            Log.Information("Forwarded ServiceResult to {ClientId} Correlation {CorrelationId}", targetClientId, result.CorrelationId);
        }
        else
        {
            await hub.Clients.All.SendAsync("ReceiveEnvelope", new Envelope(MessageType.ServiceResult, result));
            Log.Information("Broadcasted ServiceResult Correlation {CorrelationId}", result.CorrelationId);
        }

        return Results.Ok();
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Error in /api/clients/service-result for target {TargetClientId} Correlation {CorrelationId}", targetClientId, result.CorrelationId);
        return Results.Problem("An unexpected error occurred.");
    }
});

// Clients upload their start-of-task screenshot
app.MapPost("/api/clients/{clientId}/screenshot", async (string clientId, ScreenshotUpload upload) =>
{
    try
    {
        Log.Information("Screenshot from {ClientId} at {At} length={Len}", clientId, upload.CapturedAt, upload.Base64Png?.Length ?? 0);
        // For now, do not persist to disk; store could be added later
        return Results.Accepted();
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Error in /api/clients/{clientId}/screenshot for client {ClientId}", clientId);
        return Results.Problem("An unexpected error occurred.");
    }
});

// Task B: designate mouse controller (only one at a time)
app.MapPost("/api/sync/controller/{clientId}", async (string clientId, ControlHubState state, IHubContext<ControlHub> hub) =>
{
    try
    {
        if (string.Equals(clientId, "none", StringComparison.OrdinalIgnoreCase) || string.Equals(clientId, "null", StringComparison.OrdinalIgnoreCase))
        {
            state.ControllerClientId = null;
            Log.Information("Controller cleared");
            await hub.Clients.All.SendAsync("ReceiveEnvelope", new Envelope(MessageType.ControllerChanged, new { controller = (string?)null }));
            return Results.Ok(new { controller = (string?)null });
        }
        // Validate that the requested client is currently connected via the hub
        var isConnected = state.ConnectionToClient.Values.Any(v => string.Equals(v, clientId, StringComparison.OrdinalIgnoreCase));
        if (!isConnected)
        {
            Log.Warning("Attempt to set controller to non-connected client {ClientId}", clientId);
            return Results.NotFound(new { error = "Client not connected", clientId });
        }

        state.ControllerClientId = clientId;
        Log.Information("Controller set to {ClientId}", clientId);
        await hub.Clients.All.SendAsync("ReceiveEnvelope", new Envelope(MessageType.ControllerChanged, new { controller = clientId }));
        return Results.Ok(new { controller = clientId });
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Error in /api/sync/controller/{clientId} for client {ClientId}", clientId);
        return Results.Problem("An unexpected error occurred.");
    }
});

app.MapGet("/api/sync/controller", (ControlHubState state) => Results.Ok(new { controller = state.ControllerClientId }));

app.Run();

static bool ValidateUserCommand(RunCommandRequest _)
{
    // Placeholder: always true; to be implemented later
    return true;
}


public partial class Program { }
