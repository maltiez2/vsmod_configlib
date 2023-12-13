using Newtonsoft.Json.Linq;
using ProtoBuf;
using System.Collections.Generic;
using Vintagestory.API.Datastructures;

namespace ConfigLib
{
    public class ConfigSetting
    {
        public JsonObject Value { get; set; }
        public JsonObject DefaultValue { get; private set; }
        public JTokenType JsonType { get; private set; }
        public string YamlCode { get; private set; }
        public Dictionary<string, JsonObject>? Mapping { get; private set; }

        public ConfigSetting(string yamlCode, JsonObject defaultValue, JTokenType jsonType, Dictionary<string, JsonObject>? mapping = null)
        {
            Value = defaultValue;
            DefaultValue = defaultValue;
            JsonType = jsonType;
            Mapping = mapping;
            YamlCode = yamlCode;
        }
        public ConfigSetting(ConfigSettingPacket settings)
        {
            Value = new(Unwrap(JObject.Parse(settings.Value)));
            DefaultValue = new(Unwrap(JObject.Parse(settings.DefaultValue)));
            JsonType = settings.JsonType;
            YamlCode = settings.YamlCode;
            Mapping = ToMapping(settings.Mapping);
        }

        static public Dictionary<string, JsonObject>? ToMapping(Dictionary<string, string>? mapping)
        {
            if (mapping == null) return null;

            Dictionary<string, JsonObject> output = new();
            foreach ((var key, var value) in mapping)
            {
                try
                {
                    output.Add(key, new(Unwrap(JObject.Parse(value))));
                }
                catch
                {
                    output.Add(key, new(Unwrap(JObject.Parse(value))));
                }
            }
            return output;
        }

        static private JToken Unwrap(JObject token)
        {
            if (token["value"] is not JToken value) return new JValue("<invalid>");
            return value;
        }
    }

    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class ConfigSettingPacket
    {
        public string Value { get; set; } = "";
        public string DefaultValue { get; set; } = "";
        public JTokenType JsonType { get; set; } = JTokenType.Null;
        public string YamlCode { get; set; } = "";
        public Dictionary<string, string>? Mapping { get; set; }

        public ConfigSettingPacket() { }
        public ConfigSettingPacket(ConfigSetting settings)
        {
            Value = Wrap(settings.Value.Token).ToString();
            DefaultValue = Wrap(settings.DefaultValue.Token).ToString();
            JsonType = settings.JsonType;
            YamlCode = settings.YamlCode;
            Mapping = ToMapping(settings.Mapping);
        }

        static public Dictionary<string, string>? ToMapping(Dictionary<string, JsonObject>? mapping)
        {
            if (mapping == null) return null;

            Dictionary<string, string> output = new();
            foreach ((var key, var value) in mapping)
            {
                output.Add(key, Wrap(value.Token).ToString());
            }
            return output;
        }

        static private JObject Wrap(JToken token)
        {
            JObject result = new()
            {
                { "value", token }
            };
            return result;
        }
    }
}
