using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace MszCool.MqttTopicsTranslator.Entities
{
    public class MqttServerConfig
    {
        [Required]
        [RegularExpression(@"^[\w\d\s]+$", ErrorMessage = "The client ID must be alphanumeric.")]
        public string? ClientId { get; set; }

        [Required]
        public string? Server { get; set; }

        [Required]
        public int Port { get; set; }

        [Required]
        public string? Username { get; set; }

        [Required]
        public string? Password { get; set; }
    }

    public class MqttTranslatorSettings
    {
        [Required]
        public MqttServerConfig? MqttServerConfig { get; set; }

        [Required]
        public string? MqttMappingConfigFile { get; set; }
    }
}