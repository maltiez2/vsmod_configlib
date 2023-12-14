using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Vintagestory.API.Datastructures;

namespace ConfigLib
{
    static internal class ConfigParser
    {
        static public string ParseDefinition(JsonObject definition, out Dictionary<string, ConfigSetting> settings)
        {
            settings = new();
            StringBuilder result = new();
            foreach (JToken item in definition.Token)
            {
                result.Append(ParseSetting(item, settings));
                result.Append('\n');
            }

            return result.ToString();
        }

        static private string ParseSetting(JToken item, Dictionary<string, ConfigSetting> settings)
        {
            if (item is not JProperty property)
            {
                throw new InvalidConfigException("Invalid config formatting");
            }

            string token = SerializeToken(ParseToken(property, settings));
            (string first, string other) = SplitToken(token);
            string comment = GetPreComment(property.Value);
            string inline = GetInlineComment(property.Value);
            return $"{comment}{first}{inline}{other}";
        }

        static private string GetPreComment(JToken item)
        {
            if (
                item is JObject itemObject &&
                itemObject.ContainsKey("comment") &&
                itemObject["comment"] is JValue commentValue &&
                commentValue.Type == JTokenType.String &&
                commentValue.Value is string comment
                )
            {
                return $"# {comment}\n";
            }

            return "";
        }

        static private string GetInlineComment(JToken item)
        {
            if (
                item is JObject itemObject &&
                itemObject.ContainsKey("validation") &&
                itemObject["validation"] is JObject validation
                )
            {
                if (
                validation.ContainsKey("mapping") &&
                validation["mapping"] is JObject mapping
                )
                {
                    return $" # {ParseMapping(mapping)}";
                }

                if (

                    validation.ContainsKey("values") &&
                    validation["values"] is JArray values
                    )
                {
                    return $" # {ParseArray(values)}";
                }

                if (
                    validation.ContainsKey("min") ||
                    validation.ContainsKey("max")
                    )
                {
                    return $" # {ParseMinMax(validation)}";
                }
            }

            return "";
        }

        static private string ParseMinMax(JObject item)
        {
            string? min = item.ContainsKey("min") ? (item["min"] as JValue)?.Value?.ToString() : null;
            string? max = item.ContainsKey("max") ? (item["max"] as JValue)?.Value?.ToString() : null;

            return (min != null, max != null) switch
            {
                (true, true) => $"from {min} to {max}",
                (true, false) => $"greater than {min}",
                (false, true) => $"lesser than {max}",
                _ => ""
            };
        }

        static private string ParseMapping(JObject item)
        {
            StringBuilder result = new();
            result.Append("from [");
            bool first = true;
            foreach ((string name, _) in item)
            {
                if (!first) result.Append(", ");
                if (first) first = false;
                result.Append(name);
            }
            result.Append(']');
            return result.ToString();
        }

        static private string ParseArray(JArray item)
        {
            StringBuilder result = new();
            result.Append("from [");
            bool first = true;
            foreach (JToken element in item)
            {
                if (!first) result.Append(", ");
                if (first) first = false;
                result.Append((element as JValue)?.Value);
            }
            result.Append(']');
            return result.ToString();
        }

        static private (string firstLine, string remainder) SplitToken(string token)
        {
            string[] split = token.Split("\r\n", 2, StringSplitOptions.RemoveEmptyEntries);

            return (split[0], split.Length > 1 ? $"\r\n{split[1]}" : "");
        }

        static private string SerializeToken(JProperty token)
        {
            JObject tokenObject = new()
            {
                token
            };

            var simplifiedToken = ConvertJTokenToObject(tokenObject);

            var serializer = new YamlDotNet.Serialization.Serializer();

            using var writer = new StringWriter();
            serializer.Serialize(writer, simplifiedToken);
            var yaml = writer.ToString();
            return yaml;
        }

        static private JProperty ParseToken(JProperty property, Dictionary<string, ConfigSetting> settings)
        {
            string code = property.Name;
            string name = (string)((property.Value["name"] as JValue)?.Value ?? "");

            if (property.Value is not JObject propertyValue) throw new InvalidConfigException("Invalid config formatting");

            JToken value = GetValue(code, name, propertyValue, settings);
            JProperty result = new(name, value);
            return result;
        }

        static private JToken GetValue(string code, string name, JObject property, Dictionary<string, ConfigSetting> settings)
        {
            if (!property.ContainsKey("default") || property["default"] is not JToken defaultValue)
            {
                throw new InvalidConfigException($"Invalid or absent default value for '{code}' setting");
            }

            if (property.ContainsKey("validation") && property["validation"] is JObject validation)
            {
                return GetValidatedValue(code, name, defaultValue, validation, settings);
            }

            ConfigSetting setting = new(name, new(defaultValue), defaultValue.Type);
            settings.Add(code, setting);
            return defaultValue;
        }

        static private JToken GetValidatedValue(string code, string name, JToken defaultValue, JObject validation, Dictionary<string, ConfigSetting> settings)
        {
            if (validation.ContainsKey("mapping"))
            {
                if (validation["mapping"] is not JObject mapping)
                {
                    throw new InvalidConfigException($"Mapping for '{code}' setting has wrong format");
                }

                return ValidateMapping(code, name, defaultValue, mapping, settings);
            }

            ConfigSetting setting = new(name, new(defaultValue), defaultValue.Type);
            settings.Add(code, setting);
            return defaultValue;
        }

        static private JToken ValidateMapping(string code, string name, JToken defaultValue, JObject mapping, Dictionary<string, ConfigSetting> settings)
        {
            if ((defaultValue as JValue)?.Value is not string value)
            {
                throw new InvalidConfigException($"Default value for '{code}' setting should have 'string' type, because this setting has mapping in validation");
            }

            if (!mapping.ContainsKey(value) || mapping[value] is not JToken validatedValue)
            {
                if (mapping.ContainsKey(value))
                {
                    throw new InvalidConfigException($"Default value '{value}' for '{code}' setting is not valid value");
                }
                else
                {
                    throw new InvalidConfigException($"Default value '{value}' for '{code}' setting is not found in this setting mapping");
                }
            }

            Dictionary<string, JsonObject> settingMapping = new();

            foreach ((string key, JToken? mappingValue) in mapping)
            {
                if (mappingValue == null)
                {
                    throw new InvalidConfigException($"Mapping value for entry '{key}' for setting '{code}' is not valid value");
                }

                settingMapping.Add(key, new(mappingValue));
            }

            ConfigSetting setting = new(name, new(validatedValue), validatedValue.Type, settingMapping);
            settings.Add(code, setting);
            return new JValue(value);
        }

        static object ConvertJTokenToObject(JToken token)
        {
            if (token is JValue value && value.Value != null)
                return value.Value;
            if (token is JArray)
                return token.AsEnumerable().Select(ConvertJTokenToObject).ToList();
            if (token is JObject)
                return token.AsEnumerable().Cast<JProperty>().ToDictionary(x => x.Name, x => ConvertJTokenToObject(x.Value));
            throw new InvalidOperationException("Unexpected token: " + token);
        }
    }
}
