namespace MszCool.MqttTopicsTranslator.Service
{
    using System;
    using System.Reflection;
    using System.Reflection.Metadata;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using MQTTnet;
    using MQTTnet.Client;
    using MszCool.MqttTopicsTranslator.Entities;

    public class MqttWorker : BackgroundService
    {
        private readonly ILogger<MqttWorker> _logger;
        private readonly IMqttClient _mqttClient;
        private readonly MqttTranslatorSettings? _mqttTranslatorSettings;

        private bool _shallReconnect = true;
        private MqttClientOptions _mqttClientOptions;
        private IDictionary<string, MqttMapping> _mqttMappingConfig;

        public MqttWorker(ILogger<MqttWorker> logger, IMqttClient mqttClient, IOptions<MqttTranslatorSettings> mqttTranslatorSettings)
        {
            _logger = logger;
            _mqttClient = mqttClient;
            try
            {
                _mqttTranslatorSettings = mqttTranslatorSettings.Value;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error while reading the MQTT translator settings.");
                throw;
            }
            _mqttClientOptions = new MqttClientOptionsBuilder()
                        .WithClientId(_mqttTranslatorSettings?.MqttServerConfig?.ClientId)
                        .WithTcpServer(_mqttTranslatorSettings?.MqttServerConfig?.Server, _mqttTranslatorSettings?.MqttServerConfig?.Port)
                        .WithCredentials(_mqttTranslatorSettings?.MqttServerConfig?.Username, _mqttTranslatorSettings?.MqttServerConfig?.Password)
                        .WithCleanSession()
                        .Build();
            _mqttMappingConfig = new Dictionary<string, MqttMapping>();
        }

        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting the MQTT worker...");

            // Validate if the configured mapping file path is valid and exists.
            var mappingConfigPath = _mqttTranslatorSettings?.MqttMappingConfigFile ?? "";
            if (string.IsNullOrEmpty(_mqttTranslatorSettings?.MqttMappingConfigFile))
            {
                throw new ApplicationException("The MQTT mapping configuration file is not available in the application! This is required.");
            }
            if (System.IO.File.Exists(_mqttTranslatorSettings?.MqttMappingConfigFile) == false)
            {
                throw new ApplicationException("The MQTT mapping configuration file does not exist in the application! This is required.");
            }

            // Read the mapping configuration file and deserialize it using Newtonsoft into MqttMappingConfig
            var mappingConfig = System.IO.File.ReadAllText(mappingConfigPath);
            var deserializedConfig = Newtonsoft.Json.JsonConvert.DeserializeObject<MqttMappingConfig>(mappingConfig)
                    ?? throw new ApplicationException("The MQTT mapping configuration file could not be deserialized! This is required.");
            foreach (var mapping in deserializedConfig.Translations)
            {
                _mqttMappingConfig.Add(mapping.SourceTopic, mapping);
            }

            // Setup standard event handlers
            _mqttClient.ConnectedAsync += this.HandleConnectedAsync;
            _mqttClient.DisconnectedAsync += this.HandleDisconnectedAsync;
            _mqttClient.ApplicationMessageReceivedAsync += this.HandleIncomingMessage;

            // Connecting to the MQTT Server...
            await _mqttClient.ConnectAsync(_mqttClientOptions, cancellationToken);

            await base.StartAsync(cancellationToken);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Subscribe to the MQTT events
            _logger.LogInformation("Subscribing to the MQTT events...");
            _shallReconnect = true;

            // Main execution loop is not really doing anything but printing logs...
            var lastLogTime = DateTimeOffset.Now;
            while (!stoppingToken.IsCancellationRequested)
            {
                if (lastLogTime.AddMinutes(60) < DateTimeOffset.Now)
                {
                    _logger.LogInformation("Mqtt Translation Worker running at: {time}", DateTimeOffset.Now);
                    lastLogTime = DateTimeOffset.Now;
                }
                await Task.Delay(1000, stoppingToken);
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Stopping the MQTT worker...");
            // Unsubscribe from the MQTT events, and do not attempt to reconnect.
            _shallReconnect = false;
            _mqttClient.ConnectedAsync -= this.HandleConnectedAsync;
            _mqttClient.DisconnectedAsync -= this.HandleDisconnectedAsync;
            _mqttClient.ApplicationMessageReceivedAsync -= this.HandleIncomingMessage;
            await _mqttClient.DisconnectAsync();
        }

        #region Private event handling methods

        private async Task HandleConnectedAsync(MqttClientConnectedEventArgs args)
        {
            _logger.LogInformation("Connected to the MQTT server, subscribing to topics...");

            // Subscribe to the topics of interest for this worker
            foreach (var mapping in _mqttMappingConfig)
            {
                _logger.LogInformation("Subscribing to topic {topic}", mapping.Key);
                await _mqttClient.SubscribeAsync(mapping.Key);
            }
        }

        private async Task HandleDisconnectedAsync(MqttClientDisconnectedEventArgs args)
        {
            if (_shallReconnect)
            {
                _logger.LogWarning("Disconnected from the MQTT server. Reconnecting...");
                await _mqttClient.ConnectAsync(_mqttClientOptions);
            }
        }

        private async Task HandleIncomingMessage(MqttApplicationMessageReceivedEventArgs args)
        {
            _logger.LogInformation("Received message on topic {topic}: {message}", args.ApplicationMessage.Topic, args.ApplicationMessage.ConvertPayloadToString());

            if (_mqttMappingConfig.ContainsKey(args.ApplicationMessage.Topic))
            {
                var mapping = _mqttMappingConfig[args.ApplicationMessage.Topic];
                foreach (var destTopic in mapping.DestinationTopics)
                {
                    var message = new MqttApplicationMessageBuilder()
                        .WithTopic(destTopic)
                        .WithPayload(args.ApplicationMessage.PayloadSegment)
                        .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.ExactlyOnce)
                        .WithRetainFlag()
                        .Build();

                    _logger.LogInformation("Publishing message to topic {topic}: {message}", destTopic, message.ConvertPayloadToString());

                    // If there is a message value, then it needs to match the content,
                    // otherwise always send the content.
                    if (!string.IsNullOrEmpty(mapping.IfMessageValue))
                    {
                        if (string.Compare(mapping.IfMessageValue, message.ConvertPayloadToString(), true) != 0)
                        {
                            _logger.LogInformation("The message value does not match the expected value. Skipping the message.");
                            continue;
                        }
                    }

                    // Send the content if there is no filter, or if the filter matches.
                    await _mqttClient.PublishAsync(message);
                }
            }
        }

        #endregion
    }

}