using ConfigLib.Formatting;
using System;
using System.Collections.Generic;
using Vintagestory.API.Datastructures;

namespace ConfigLib;

public interface IConfigProvider
{
    IEnumerable<string> Domains { get; }
    IConfig? GetConfig(string domain);
    ISetting? GetSetting(string domain, string code);
    IConfig? GetServerConfig(string domain);
    ISetting? GetServerSetting(string domain, string code);
    void RegisterCustomConfig(string domain, Action<string, ControlButtons> drawDelegate);
    
    event Action? ConfigsLoaded;
}

public interface IConfig
{
    string ConfigFilePath { get; }
    public int Version { get; }

    void WriteToFile();
    bool ReadFromFile();
    void RestoreToDefaults();
    ISetting? GetSetting(string code);
}

public enum ConfigSettingType
{
    Boolean,
    Float,
    Integer,
    String,
    Other
}

public interface ISetting : IConfigBlock
{
    JsonObject Value { get; set; }
    JsonObject DefaultValue { get; }
    ConfigSettingType SettingType { get; }
    string YamlCode { get; }
    Validation? Validation { get; }
}
