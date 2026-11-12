using Newtonsoft.Json.Linq;
using PaceLaps.Plugin.Core;

namespace PaceLaps.Plugin.Core.Tests;

public sealed class PusherProtocolTests
{
    [Fact]
    public void The_socket_address_is_the_sites_own_app_path()
    {
        Assert.Equal("wss://app.pacelaps.com/app/key1?protocol=7&client=pacelaps-simhub&version=0.1.0&flash=false",
            PusherProtocol.SocketUri(new Uri("https://app.pacelaps.com/"), "key1", "0.1.0").ToString());
        Assert.Equal("ws://localhost:8081/app/key1?protocol=7&client=pacelaps-simhub&version=0.1.0&flash=false",
            PusherProtocol.SocketUri(new Uri("http://localhost:8081/"), "key1", "0.1.0").ToString());
    }

    [Fact]
    public void Connection_established_gives_the_socket_id_and_the_activity_timeout()
    {
        var frame = PusherProtocol.Parse("{\"event\":\"pusher:connection_established\",\"data\":\"{\\\"socket_id\\\":\\\"123.456\\\",\\\"activity_timeout\\\":30}\"}")!;

        Assert.True(PusherProtocol.TryConnectionEstablished(frame, out var id, out var timeout));
        Assert.Equal("123.456", id);
        Assert.Equal(30, timeout);
    }

    [Fact]
    public void Event_data_is_json_text_whether_sent_as_a_string_or_an_object()
    {
        var asString = PusherProtocol.Parse("{\"event\":\"message\",\"channel\":\"private-event.1.all\",\"data\":\"{\\\"id\\\":\\\"m1\\\"}\"}")!;
        var asObject = PusherProtocol.Parse("{\"event\":\"message\",\"channel\":\"private-event.1.all\",\"data\":{\"id\":\"m1\"}}")!;

        Assert.Equal("private-event.1.all", asString.Channel);
        Assert.Equal("m1", (string?)JObject.Parse(asString.Data!)["id"]);
        Assert.Equal("m1", (string?)JObject.Parse(asObject.Data!)["id"]);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("{\"data\":{}}")]
    public void Garbage_is_skipped(string text) => Assert.Null(PusherProtocol.Parse(text));

    [Fact]
    public void Subscribe_carries_the_channel_and_its_auth()
    {
        var frame = JObject.Parse(PusherProtocol.Subscribe("private-event.1.all", "key:sig"));

        Assert.Equal("pusher:subscribe", (string?)frame["event"]);
        Assert.Equal("private-event.1.all", (string?)frame["data"]!["channel"]);
        Assert.Equal("key:sig", (string?)frame["data"]!["auth"]);
    }
}
