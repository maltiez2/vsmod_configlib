using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using Vintagestory.API.Datastructures;

namespace ConfigLib
{
    public interface IConfigProvider
    {
        IEnumerable<string> Domains { get; }
        IConfig? GetConfig(string domain);
        ISetting? GetSetting(string domain, string code);
    }
    
    public interface IConfig
    {
        string ConfigFilePath {  get; }
        string ConfigFileContent { get; }
        bool LoadedFromFile { get; }

        void WriteToFile();
        bool ReadFromFile();
        void RestoreToDefault();
        ISetting? GetSetting(string code);
    }

    public interface ISetting
    {
        JsonObject Value { get; set; }
        JsonObject DefaultValue { get; }
        JTokenType JsonType { get; }
        string YamlCode { get; }
        Validation? Validation { get; }
    }
}
