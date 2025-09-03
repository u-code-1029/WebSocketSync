namespace Client;

public class ClientSettings
{
    public string? ClientId { get; set; }
    public string ServerUrl { get; set; } = "http://*:2665/hub";
    public string? ResultCallback { get; set; }
    public string? SyncDirectory { get; set; }
    public bool SyncEnabled { get; set; } = false;
    public bool Controller { get; set; }
    public double? MouseMoveHz { get; set; }
    public bool OverwriteExistingOnly { get; set; } = true;
}
