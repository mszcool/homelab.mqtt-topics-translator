namespace MszCool.MqttTopicsTranslator;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;

using MQTTnet;
using MQTTnet.Client;

using MszCool.MqttTopicsTranslator.Service;
using MszCool.MqttTopicsTranslator.Entities;

class Program
{
    static void Main(string[] args)
    {
        CreateHostBuilder(args).Build().Run();
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((hostingContext, config) =>
            {
                // If in development, load appsettings.development.json
                if (hostingContext.HostingEnvironment.IsDevelopment())
                {
                    config.AddJsonFile("appsettings.development.json", optional: true, reloadOnChange: true);
                }
                else
                {
                    config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                }
                config.AddEnvironmentVariables();
            })
            .ConfigureServices((hostContext, services) =>
            {
                // First of all, validate the configuration options
                services.AddOptions<MqttTranslatorSettings>()
                        .Bind(hostContext.Configuration.GetSection("MqttTranslatorSettings"))
                        .ValidateDataAnnotations()
                        .Validate(settings =>
                        {
                            var valResult = settings.MqttServerConfig != null && settings.MqttServerConfig.Port > 0;
                            valResult = valResult && System.IO.File.Exists(settings.MqttMappingConfigFile);
                            return valResult;
                        });

                // Now get and validate the options
                var mqttTranslatorSettings = hostContext.Configuration.GetSection("MqttTranslatorSettings").Get<MqttTranslatorSettings>();
                if (mqttTranslatorSettings == null)
                {
                    throw new ApplicationException("MqttTranslatorSettings are not available in the application! These are required.");
                }

                // The MQTT client should be a central service to all components in the app.
                services.Add(new ServiceDescriptor(typeof(IMqttClient), sp =>
                {
                    // Add the MQTT client as a central service to the app
                    var factory = new MqttFactory();
                    var mqttClient = factory.CreateMqttClient();
                    return mqttClient;
                }, ServiceLifetime.Singleton));

                // Add the MQTT worker to the services
                services.AddHostedService<MqttWorker>();
            });
}
