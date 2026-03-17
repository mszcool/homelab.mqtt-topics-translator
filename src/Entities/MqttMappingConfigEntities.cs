namespace MszCool.MqttTopicsTranslator.Entities
{
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;

    public class MqttMappingConfig
    {
        [Required]
        public string Description { get; set; } = "Default MQTT Mapping Configuration";
        
        [Required]
        public List<MqttMapping> Translations { get; set; } = [];
    }

    public class MqttMapping
    {
        [Required]
        [RegularExpression(@"^[\w\d\s]+$", ErrorMessage = "The source topic must be alphanumeric.")]
        public string SourceTopic { get; set; } = "";

        public string IfMessageValue { get; set; } = "";

        [Required]
        public List<string> DestinationTopics { get; set; } = [];
    }
}