using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Shared;
using Xunit;

namespace Server.Tests;

public class CommandValidationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public CommandValidationTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ValidateUserCommand_Default_ReturnsAccepted()
    {
        var resp = await _client.PostAsJsonAsync("/api/commands/run-cmd", new RunCommandRequest("cmd", "/c echo ok"));
        Assert.Equal(System.Net.HttpStatusCode.Accepted, resp.StatusCode);
    }
}

