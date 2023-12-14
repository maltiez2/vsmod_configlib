using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.IO;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using YamlDotNet.Serialization;

namespace ConfigLib
{
    public interface IConfigProvider
    {
        IEnumerable<string> Domains { get; }
        IConfig? GetConfig(string domain);
    }
    
    public interface IConfig
    {
        string ConfigFilePath {  get; }
        string ConfigFileContent { get; }
        bool LoadedFromFile { get; }

        void WriteToFile();
        bool ReadFromFile();
        ISetting? GetSetting(string code);
    }

    public interface ISetting
    {
        public JsonObject Value { get; set; }
        public JsonObject DefaultValue { get; }
        public JTokenType JsonType { get; }
        public string YamlCode { get; }
        public Dictionary<string, JsonObject>? Mapping { get; }
    }
}
