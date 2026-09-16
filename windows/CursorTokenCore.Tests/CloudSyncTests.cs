using System.Net;
using System.Text;
using System.Text.Json;
using CursorTokenCore;
using Xunit;

namespace CursorTokenCore.Tests;

public class CloudSyncTests
{
    [Fact]
    public async Task ReconcileRetriesPutAfter409ThenSucceeds()
    {
        var cfg = LoggedIn();
        var handler = new ScriptedHandler
        {
            Steps =
            [
                (HttpMethod.Get, "/v1/sync", 200, """{"revision":1}"""),
                (HttpMethod.Put, "/v1/sync", 409, """{"detail":"同步冲突，请重试"}"""),
                (HttpMethod.Get, "/v1/sync", 200, """{"revision":2}"""),
                (HttpMethod.Put, "/v1/sync", 200, """{"revision":3}"""),
            ],
        };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        CloudSync.TestHttp = http;
        try
        {
            var status = await CloudSync.ReconcileAsync(cfg);
            Assert.True(status.Ok, status.Message);
            Assert.True(status.Pushed);
            Assert.Equal(3, cfg.CloudRevision);
            Assert.Equal(["GET /v1/sync", "PUT /v1/sync", "GET /v1/sync", "PUT /v1/sync"], handler.Calls);
        }
        finally
        {
            CloudSync.TestHttp = null;
        }
    }

    [Fact]
    public async Task Reconcile409ThenMatchingRemoteSkipsSecondPut()
    {
        var cfg = LoggedIn();
        var local = AccountSync.SnapshotFromConfig(cfg);
        local.DeviceId = cfg.SyncDeviceId;
        var envelope = AccountSync.EncryptEnvelope(local, cfg.SyncSecret);
        var secondGet = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["revision"] = 2,
            ["envelope"] = envelope,
        });
        var handler = new ScriptedHandler
        {
            Steps =
            [
                (HttpMethod.Get, "/v1/sync", 200, """{"revision":1}"""),
                (HttpMethod.Put, "/v1/sync", 409, """{"detail":"同步冲突，请重试"}"""),
                (HttpMethod.Get, "/v1/sync", 200, secondGet),
            ],
        };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        CloudSync.TestHttp = http;
        try
        {
            var status = await CloudSync.ReconcileAsync(cfg);
            Assert.True(status.Ok, status.Message);
            Assert.False(status.Pushed);
            Assert.Equal(2, cfg.CloudRevision);
            Assert.Equal(["GET /v1/sync", "PUT /v1/sync", "GET /v1/sync"], handler.Calls);
        }
        finally
        {
            CloudSync.TestHttp = null;
        }
    }

    static AppConfig LoggedIn() => new()
    {
        SyncEnabled = true,
        SyncSecret = "unit-test-secret",
        CloudEmail = "t@example.com",
        CloudAccessToken = "access-token",
        CloudRefreshToken = "refresh-token",
        SyncDeviceId = "device-test",
    };

    sealed class ScriptedHandler : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Path, int Status, string Body)> Steps { get; init; } = [];
        public List<string> Calls { get; } = [];
        int _i;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var step = Steps[_i++];
            Calls.Add($"{request.Method.Method} {request.RequestUri!.AbsolutePath}");
            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)step.Status)
            {
                Content = new StringContent(step.Body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
