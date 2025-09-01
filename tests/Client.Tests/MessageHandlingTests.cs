using Client;
using Xunit;

namespace Client.Tests;

public class MessageHandlingTests
{
    [Fact]
    public void ViewModel_AppendsEvents()
    {
        var vm = new ClientViewModel();
        vm.AddEventCommand.Execute("hello");
        Assert.Contains("hello", vm.Events);
    }
}
