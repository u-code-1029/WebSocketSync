using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Shared;
using Xunit;

namespace Server.Tests;

public class ServiceRunTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public ServiceRunTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ServiceRun_ReturnsAccepted()
    {
        var req = new RunServiceRequest("ExampleService", Guid.NewGuid().ToString("N"), null);
        var resp = await _client.PostAsJsonAsync("/api/tasks/service-run", req);
        Assert.Equal(System.Net.HttpStatusCode.Accepted, resp.StatusCode);
    }
}

