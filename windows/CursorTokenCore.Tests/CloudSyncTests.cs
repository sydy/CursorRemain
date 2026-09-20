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

    [Fact]
    public void DecryptsGzipEnvelopeWhenCompressionFieldMissing()
    {
        var accounts = Enumerable.Range(0, 12).Select(i => new SyncAccount
        {
            Id = $"user_{i:00}",
            Label = $"账号{i}",
            Token = "tok-" + i + "-" + new string('x', 80),
            MembershipType = "pro",
            SyncUpdatedAt = "2026-09-09T00:00:00.000Z",
        }).ToList();
        var payload = new SyncSnapshot
        {
            Version = 1,
            UpdatedAt = "2026-09-09T00:00:00.000Z",
            DeviceId = "dev-gzip",
            ActiveAccountId = "user_00",
            Accounts = accounts,
        };
        var env = AccountSync.EncryptEnvelope(payload, "gzip-pass-123", iterations: 1000);
        Assert.Equal("gzip", env["compression"]?.ToString());
        env.Remove("compression");
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(env));
        var got = AccountSync.DecryptEnvelope(doc.RootElement, "gzip-pass-123");
        Assert.Equal("user_00", got.Accounts[0].Id);
        Assert.Equal(12, got.Accounts.Count);
    }

    [Fact]
    public void TrimSnapshotKeepsNewestEvents()
    {
        var events = Enumerable.Range(0, 800).Select(i => new UsageEvent
        {
            Id = $"e{i}",
            TimestampMs = (i + 1) * 1000,
            Model = "opus",
            Kind = "included",
            Tokens = 1,
        }).ToList();
        var snap = new SyncSnapshot
        {
            Version = 1,
            UpdatedAt = "2026-09-09T00:00:00.000Z",
            ActiveAccountId = "user_01A",
            Usage = [new SyncUsage { AccountId = "user_01A", Events = events }],
        };
        var trimmed = AccountSync.TrimSnapshotForUpload(snap, 4000);
        var kept = trimmed.Usage?.FirstOrDefault()?.Events ?? [];
        Assert.NotEmpty(kept);
        Assert.True(AccountSync.UsageRecordCount(trimmed) < AccountSync.UsageRecordCount(snap));
        Assert.Equal("e799", kept[^1].Id);
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
