namespace MszCool.MqttTopicsTranslator.Tests;

using System.Text;
using MQTTnet;
using MQTTnet.Client;
using MszCool.MqttTopicsTranslator.Entities;
using Newtonsoft.Json;
using Xunit;

/// <summary>
/// Integration tests that run against Docker Compose services (mosquitto + mqtt-translator).
/// Start the stack with "docker compose up" before running these tests.
/// The MQTT broker is expected on localhost:1883.
/// </summary>
public class MqttTranslationIntegrationTests : IAsyncLifetime
{
    private const string BrokerHost = "localhost";
    private const int BrokerPort = 1883;
    private const int TimeoutSeconds = 30;
    private const string MappingFile = "sample-mapping.json";

    private IMqttClient _publisherClient = null!;
    private IMqttClient _subscriberClient = null!;
    private MqttMappingConfig _mappingConfig = null!;

    public async Task InitializeAsync()
    {
        var json = await File.ReadAllTextAsync(MappingFile);
        _mappingConfig = JsonConvert.DeserializeObject<MqttMappingConfig>(json)
            ?? throw new InvalidOperationException("Failed to deserialize mapping config.");

        var factory = new MqttFactory();
        _publisherClient = factory.CreateMqttClient();
        _subscriberClient = factory.CreateMqttClient();

        var publisherOptions = new MqttClientOptionsBuilder()
            .WithClientId("TestPublisher")
            .WithTcpServer(BrokerHost, BrokerPort)
            .WithCleanSession()
            .Build();

        var subscriberOptions = new MqttClientOptionsBuilder()
            .WithClientId("TestSubscriber")
            .WithTcpServer(BrokerHost, BrokerPort)
            .WithCleanSession()
            .Build();

        await _publisherClient.ConnectAsync(publisherOptions);
        await _subscriberClient.ConnectAsync(subscriberOptions);
    }

    public async Task DisposeAsync()
    {
        if (_publisherClient.IsConnected)
            await _publisherClient.DisconnectAsync();
        if (_subscriberClient.IsConnected)
            await _subscriberClient.DisconnectAsync();

        _publisherClient.Dispose();
        _subscriberClient.Dispose();
    }

    [Fact]
    public async Task Test1_SourceTranslatesToMultipleDestinations()
    {
        // pooltranslatortest/test1 -> pooltranslatortestresult/cmnd/test1, pooltranslatortestresult/cmnd/test2
        var mapping = _mappingConfig.Translations[0];
        var payload = "hello-test1";

        var receivedTopics = new HashSet<string>();
        var allReceived = new TaskCompletionSource<bool>();

        _subscriberClient.ApplicationMessageReceivedAsync += args =>
        {
            receivedTopics.Add(args.ApplicationMessage.Topic);
            if (receivedTopics.Count >= mapping.DestinationTopics.Count)
                allReceived.TrySetResult(true);
            return Task.CompletedTask;
        };

        foreach (var dest in mapping.DestinationTopics)
            await _subscriberClient.SubscribeAsync(dest);

        // Small delay to let subscriptions settle
        await Task.Delay(500);

        await _publisherClient.PublishAsync(new MqttApplicationMessageBuilder()
            .WithTopic(mapping.SourceTopic)
            .WithPayload(payload)
            .Build());

        var completed = await Task.WhenAny(allReceived.Task, Task.Delay(TimeSpan.FromSeconds(TimeoutSeconds)));
        Assert.True(completed == allReceived.Task, $"Timed out waiting for translated messages on destinations of {mapping.SourceTopic}");

        foreach (var dest in mapping.DestinationTopics)
            Assert.Contains(dest, receivedTopics);
    }

    [Fact]
    public async Task Test2_SourceTranslatesToSingleDestination()
    {
        // pooltranslatortest/test2 -> pooltranslatortestresult/stat/test1
        var mapping = _mappingConfig.Translations[1];
        var payload = "hello-test2";

        var received = new TaskCompletionSource<string>();

        _subscriberClient.ApplicationMessageReceivedAsync += args =>
        {
            if (args.ApplicationMessage.Topic == mapping.DestinationTopics[0])
                received.TrySetResult(args.ApplicationMessage.ConvertPayloadToString());
            return Task.CompletedTask;
        };

        await _subscriberClient.SubscribeAsync(mapping.DestinationTopics[0]);
        await Task.Delay(500);

        await _publisherClient.PublishAsync(new MqttApplicationMessageBuilder()
            .WithTopic(mapping.SourceTopic)
            .WithPayload(payload)
            .Build());

        var completed = await Task.WhenAny(received.Task, Task.Delay(TimeSpan.FromSeconds(TimeoutSeconds)));
        Assert.True(completed == received.Task, $"Timed out waiting for translated message on {mapping.DestinationTopics[0]}");
        Assert.Equal(payload, received.Task.Result);
    }

    [Fact]
    public async Task Test3_AnotherSourceTranslatesToSameDestination()
    {
        // pooltranslatortest/test3 -> pooltranslatortestresult/stat/test1
        var mapping = _mappingConfig.Translations[2];
        var payload = "hello-test3";

        var received = new TaskCompletionSource<string>();

        _subscriberClient.ApplicationMessageReceivedAsync += args =>
        {
            var msg = args.ApplicationMessage.ConvertPayloadToString();
            if (args.ApplicationMessage.Topic == mapping.DestinationTopics[0] && msg == payload)
                received.TrySetResult(msg);
            return Task.CompletedTask;
        };

        await _subscriberClient.SubscribeAsync(mapping.DestinationTopics[0]);
        await Task.Delay(500);

        await _publisherClient.PublishAsync(new MqttApplicationMessageBuilder()
            .WithTopic(mapping.SourceTopic)
            .WithPayload(payload)
            .Build());

        var completed = await Task.WhenAny(received.Task, Task.Delay(TimeSpan.FromSeconds(TimeoutSeconds)));
        Assert.True(completed == received.Task, $"Timed out waiting for translated message on {mapping.DestinationTopics[0]}");
        Assert.Equal(payload, received.Task.Result);
    }

    [Fact]
    public async Task Test4_ConditionalTranslation_MatchingPayload_Succeeds()
    {
        // pooltranslatortest/test4 with matching ifMessageValue -> pooltranslatortestresult/stat/test42
        var mapping = _mappingConfig.Translations[3];
        var payload = mapping.IfMessageValue; // "only if this is here"

        var received = new TaskCompletionSource<string>();

        _subscriberClient.ApplicationMessageReceivedAsync += args =>
        {
            if (args.ApplicationMessage.Topic == mapping.DestinationTopics[0])
                received.TrySetResult(args.ApplicationMessage.ConvertPayloadToString());
            return Task.CompletedTask;
        };

        await _subscriberClient.SubscribeAsync(mapping.DestinationTopics[0]);
        await Task.Delay(500);

        await _publisherClient.PublishAsync(new MqttApplicationMessageBuilder()
            .WithTopic(mapping.SourceTopic)
            .WithPayload(payload)
            .Build());

        var completed = await Task.WhenAny(received.Task, Task.Delay(TimeSpan.FromSeconds(TimeoutSeconds)));
        Assert.True(completed == received.Task, $"Timed out waiting for conditional translated message on {mapping.DestinationTopics[0]}");
        Assert.Equal(payload, received.Task.Result);
    }

    [Fact]
    public async Task Test4_ConditionalTranslation_NonMatchingPayload_NoTranslation()
    {
        // pooltranslatortest/test4 with non-matching payload -> should NOT arrive at destination
        var mapping = _mappingConfig.Translations[3];
        var payload = "this should not match";

        var received = new TaskCompletionSource<string>();

        _subscriberClient.ApplicationMessageReceivedAsync += args =>
        {
            if (args.ApplicationMessage.Topic == mapping.DestinationTopics[0])
                received.TrySetResult(args.ApplicationMessage.ConvertPayloadToString());
            return Task.CompletedTask;
        };

        await _subscriberClient.SubscribeAsync(mapping.DestinationTopics[0]);
        await Task.Delay(500);

        await _publisherClient.PublishAsync(new MqttApplicationMessageBuilder()
            .WithTopic(mapping.SourceTopic)
            .WithPayload(payload)
            .Build());

        // Wait a reasonable time — message should NOT arrive
        var completed = await Task.WhenAny(received.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.False(completed == received.Task, "Message was translated when it should have been filtered out");
    }
}
