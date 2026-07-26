using System.Net;
using System.Text.Json;
using GoveeController.Domain.Devices;
using GoveeController.Infrastructure.Govee;
using Microsoft.Extensions.Options;
using Xunit;

namespace GoveeController.Application.Tests.Govee;

public class GoveeApiClientTests
{
    private static GoveeApiClient CreateClient(FakeHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);
        var options = Options.Create(new GoveeApiOptions { ApiKey = "test-api-key", BaseUrl = "https://example.test" });
        return new GoveeApiClient(httpClient, options);
    }

    [Fact]
    public async Task GetDevicesAsync_SendsApiKeyHeader_AndMapsCapabilities()
    {
        const string json = """
        {
          "code": 200,
          "message": "success",
          "data": [
            {
              "sku": "H6159",
              "device": "AA:BB:CC:DD:EE:FF:00:11",
              "deviceName": "Desk Light",
              "type": "devices.types.light",
              "capabilities": [
                { "type": "devices.capabilities.on_off", "instance": "powerSwitch" },
                {
                  "type": "devices.capabilities.range",
                  "instance": "brightness",
                  "parameters": { "dataType": "INTEGER", "range": { "min": 1, "max": 100 } }
                },
                { "type": "devices.capabilities.some_unmodeled_thing", "instance": "workMode" }
              ]
            }
          ]
        }
        """;
        var handler = new FakeHttpMessageHandler(json);
        var client = CreateClient(handler);

        var devices = await client.GetDevicesAsync();

        Assert.Equal("test-api-key", handler.LastRequest!.Headers.GetValues("Govee-API-Key").Single());
        var device = Assert.Single(devices);
        Assert.Equal("H6159", device.Sku);
        Assert.Equal("Desk Light", device.Name);
        Assert.True(device.SupportsPower);
        Assert.True(device.SupportsBrightness);
        var brightness = device.Capabilities.Single(c => c.Kind == CapabilityKind.Brightness);
        Assert.Equal(1, brightness.Min);
        Assert.Equal(100, brightness.Max);
        // The unrecognized capability type should be dropped, not crash mapping.
        Assert.DoesNotContain(device.Capabilities, c => c.Kind == CapabilityKind.Unsupported);
    }

    [Fact]
    public async Task SetPowerAsync_SendsOnOffControlRequest()
    {
        var handler = new FakeHttpMessageHandler("""{"requestId":"x","msg":"success","code":200}""");
        var client = CreateClient(handler);

        await client.SetPowerAsync("H6159", "AA:BB:CC:DD:EE:FF:00:11", powerOn: true);

        Assert.Equal("/router/api/v1/device/control", handler.LastRequest!.RequestUri!.AbsolutePath);
        using var body = JsonDocument.Parse(handler.LastRequestBody!);
        var capability = body.RootElement.GetProperty("payload").GetProperty("capability");
        Assert.Equal("devices.capabilities.on_off", capability.GetProperty("type").GetString());
        Assert.Equal("powerSwitch", capability.GetProperty("instance").GetString());
        Assert.Equal(1, capability.GetProperty("value").GetInt32());
    }

    [Fact]
    public async Task TriggerSceneAsync_SendsSceneValueObject()
    {
        var handler = new FakeHttpMessageHandler("""{"requestId":"x","msg":"success","code":200}""");
        var client = CreateClient(handler);
        var scene = new GoveeScene("Sunset", ParamId: 4280, Id: 3853);

        await client.TriggerSceneAsync("H6159", "AA:BB:CC:DD:EE:FF:00:11", scene);

        using var body = JsonDocument.Parse(handler.LastRequestBody!);
        var value = body.RootElement.GetProperty("payload").GetProperty("capability").GetProperty("value");
        Assert.Equal(4280, value.GetProperty("paramId").GetInt32());
        Assert.Equal(3853, value.GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task GetDeviceStateAsync_Throws_OnNonSuccessStatusCode()
    {
        var handler = new FakeHttpMessageHandler("""{"code":401,"message":"invalid api key"}""", HttpStatusCode.Unauthorized);
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetDeviceStateAsync("H6159", "AA:BB:CC:DD:EE:FF:00:11"));
    }

    [Fact]
    public async Task SetPowerAsync_Throws_WhenGoveeReportsBodyLevelFailureUnderHttp200()
    {
        // Govee's control endpoint returns HTTP 200 even when the command fails (e.g. the
        // physical device is offline) — the real result is the "code"/"msg" in the JSON body.
        const string json = """
        {
          "requestId": "x",
          "msg": "Device is offline. Please check the Wi-Fi connection.",
          "code": 400,
          "capability": { "type": "devices.capabilities.on_off", "instance": "powerSwitch", "state": { "status": "failure" }, "value": 1 }
        }
        """;
        var handler = new FakeHttpMessageHandler(json, HttpStatusCode.OK);
        var client = CreateClient(handler);

        var ex = await Assert.ThrowsAsync<GoveeApiException>(() => client.SetPowerAsync("H600A", "AA:BB:CC:DD:EE:FF:00:11", powerOn: true));
        Assert.Equal(400, ex.GoveeCode);
        Assert.Contains("offline", ex.Message);
    }
}
