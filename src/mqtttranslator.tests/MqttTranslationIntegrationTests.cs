namespace MszCool.MqttTopicsTranslator.Tests;

using System.Text;
using MQTTnet;
using MQTTnet.Client;
using MszCool.MqttTopicsTranslator.Entities;
using Newtonsoft.Json;
using Xunit;
using Xunit.Abstractions;

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

    private readonly ITestOutputHelper _output;
    private IMqttClient _publisherClient = null!;
    private IMqttClient _subscriberClient = null!;
    private MqttMappingConfig _mappingConfig = null!;

    public MqttTranslationIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

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
        var mapping = _mappingConfig.Translations[0];
        var payload = "hello-test1";
        await AssertTranslation(mapping, payload);
    }

    [Fact]
    public async Task Test2_SourceTranslatesToSingleDestination()
    {
        var mapping = _mappingConfig.Translations[1];
        var payload = "hello-test2";
        await AssertTranslation(mapping, payload);
    }

    [Fact]
    public async Task Test3_AnotherSourceTranslatesToSameDestination()
    {
        var mapping = _mappingConfig.Translations[2];
        var payload = "hello-test3";
        await AssertTranslation(mapping, payload);
    }

    [Fact]
    public async Task Test4_ConditionalTranslation_MatchingPayload_Succeeds()
    {
        var mapping = _mappingConfig.Translations[3];
        var payload = mapping.IfMessageValue; // "only if this is here"
        await AssertTranslation(mapping, payload);
    }

    [Fact]
    public async Task Test4_ConditionalTranslation_NonMatchingPayload_NoTranslation()
    {
        var mapping = _mappingConfig.Translations[3];
        var payload = "this should not match";
        await AssertNoTranslation(mapping, payload);
    }

    /// <summary>
    /// Publishes a message on the mapping's source topic and asserts that all
    /// destination topics receive the expected payload within the timeout.
    /// </summary>
    private async Task AssertTranslation(MqttMapping mapping, string payload)
    {
        _output.WriteLine($"[PUBLISH]  topic='{mapping.SourceTopic}' payload='{payload}'");
        _output.WriteLine($"[EXPECT]   {mapping.DestinationTopics.Count} destination(s): {string.Join(", ", mapping.DestinationTopics)}");

        var receivedPayloads = new Dictionary<string, string>();
        var allReceived = new TaskCompletionSource<bool>();
        var expectedCount = mapping.DestinationTopics.Count;

        _subscriberClient.ApplicationMessageReceivedAsync += args =>
        {
            var msg = args.ApplicationMessage.ConvertPayloadToString();
            var topic = args.ApplicationMessage.Topic;
            if (mapping.DestinationTopics.Contains(topic) && msg == payload)
            {
                _output.WriteLine($"[RECEIVED] topic='{topic}' payload='{msg}'");
                receivedPayloads[topic] = msg;
                if (receivedPayloads.Count >= expectedCount)
                    allReceived.TrySetResult(true);
            }
            return Task.CompletedTask;
        };

        foreach (var dest in mapping.DestinationTopics)
            await _subscriberClient.SubscribeAsync(dest);

        await Task.Delay(500);

        await _publisherClient.PublishAsync(new MqttApplicationMessageBuilder()
            .WithTopic(mapping.SourceTopic)
            .WithPayload(payload)
            .Build());

        var completed = await Task.WhenAny(allReceived.Task, Task.Delay(TimeSpan.FromSeconds(TimeoutSeconds)));
        Assert.True(completed == allReceived.Task,
            $"Timed out waiting for translated messages on destinations of {mapping.SourceTopic}");

        foreach (var dest in mapping.DestinationTopics)
        {
            Assert.True(receivedPayloads.ContainsKey(dest), $"No message received on {dest}");
            Assert.Equal(payload, receivedPayloads[dest]);
        }

        _output.WriteLine($"[PASS]     All {expectedCount} destination(s) received the expected payload.");
    }

    /// <summary>
    /// Publishes a message on the mapping's source topic and asserts that NO
    /// destination topic receives a matching message within a short window.
    /// </summary>
    private async Task AssertNoTranslation(MqttMapping mapping, string payload)
    {
        _output.WriteLine($"[PUBLISH]  topic='{mapping.SourceTopic}' payload='{payload}'");
        _output.WriteLine($"[EXPECT]   NO translation to: {string.Join(", ", mapping.DestinationTopics)}");

        var received = new TaskCompletionSource<string>();

        _subscriberClient.ApplicationMessageReceivedAsync += args =>
        {
            var msg = args.ApplicationMessage.ConvertPayloadToString();
            if (mapping.DestinationTopics.Contains(args.ApplicationMessage.Topic) && msg == payload)
            {
                _output.WriteLine($"[RECEIVED] topic='{args.ApplicationMessage.Topic}' payload='{msg}' (unexpected!)");
                received.TrySetResult(msg);
            }
            return Task.CompletedTask;
        };

        foreach (var dest in mapping.DestinationTopics)
            await _subscriberClient.SubscribeAsync(dest);

        await Task.Delay(500);

        await _publisherClient.PublishAsync(new MqttApplicationMessageBuilder()
            .WithTopic(mapping.SourceTopic)
            .WithPayload(payload)
            .Build());

        var completed = await Task.WhenAny(received.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.False(completed == received.Task, "Message was translated when it should have been filtered out");

        _output.WriteLine($"[PASS]     No translated message arrived (as expected).");
    }
}
