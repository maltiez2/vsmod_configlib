using System;
using System.Collections.Generic;
using Vintagestory.API.Datastructures;

namespace ConfigLib;

public interface IConfigProvider
{
    IEnumerable<string> Domains { get; }
    IConfig? GetConfig(string domain);
    ISetting? GetSetting(string domain, string code);
    public void RegisterCustomConfig(string domain, Action<string, ControlButtons> drawDelegate);
}

public interface IConfig
{
    string ConfigFilePath { get; }
    string ConfigFileContent { get; }
    bool LoadedFromFile { get; }

    void WriteToFile();
    bool ReadFromFile();
    void RestoreToDefault();
    ISetting? GetSetting(string code);
}

public enum ConfigSettingType
{
    Boolean,
    Float,
    Integer,
    Other
}

public interface ISetting
{
    JsonObject Value { get; set; }
    JsonObject DefaultValue { get; }
    ConfigSettingType SettingType { get; }
    string YamlCode { get; }
    Validation? Validation { get; }
}
