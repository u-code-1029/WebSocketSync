using Client;
using Microsoft.Extensions.Options;
using Shared;
using System.Text.Json;
using Xunit;

namespace Client.Tests;

public class TaskB_SyncTests
{
    [Fact]
    public void MouseEvent_AddsLogEntry()
    {
        var vm = new ClientViewModel();
        var fileSyncManager = new FileSyncManager();
        var worker = new ClientWorker(vm, Options.Create(new ClientSettings()), fileSyncManager);
        var msg = new Envelope(MessageType.MouseEvent, new MouseEventMessage("master", MouseAction.Move, 0.1, 0.2, 0));
        var json = JsonSerializer.Serialize(msg, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        //worker.HandleMessage(json);
        Assert.Contains(vm.Events, e => e.Contains("MouseEvent"));
    }

    [Fact]
    public void FileSync_AddsLogEntry()
    {
        var vm = new ClientViewModel();
        var fileSyncManager = new FileSyncManager();
        var worker = new ClientWorker(vm, Options.Create(new ClientSettings()), fileSyncManager);
        var msg = new Envelope(MessageType.FileSync, new FileSyncMessage("sender", "a.txt", FileSyncOp.Create, ""));
        var json = JsonSerializer.Serialize(msg, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        //worker.HandleMessage(json);
        Assert.Contains(vm.Events, e => e.Contains("FileSync"));
    }
}
