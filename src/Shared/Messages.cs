using System.Text.Json.Serialization;

namespace Shared;

public enum MessageType
{
    Heartbeat,
    ClientHello,
    ControllerChanged,
    RunCommand,
    RunService,
    ServiceResult,
    Screenshot,
    MouseEvent,
    FileSync
}

public record Envelope(
    [property: JsonPropertyName("type")] MessageType Type,
    [property: JsonPropertyName("payload")] object Payload
);

public record ClientHello(
    string ClientId,
    string MachineName,
    string? UserName
);

public record RunCommandRequest(
    string Command,
    string[]? Arguments
);

public record RunServiceRequest(
    string ServiceName,
    string CorrelationId,
    Dictionary<string,string>? Parameters
);

public record ServiceResult(
    string ServiceName,
    string CorrelationId,
    bool Success,
    string? Message
);

public record ScreenshotUpload(
    string ClientId,
    string Base64Png,
    DateTimeOffset CapturedAt
);

public enum MouseAction { Move, LeftDown, LeftUp, RightDown, RightUp, Wheel }

public record MouseEventMessage(
    string ControllerClientId,
    MouseAction Action,
    double NormalizedX,
    double NormalizedY,
    int Delta
);

public enum FileSyncOp { Create, Update, Delete }

public record FileSyncMessage(
    string SenderClientId,
    string RelativePath,
    FileSyncOp Operation,
    string? Base64Content
);
