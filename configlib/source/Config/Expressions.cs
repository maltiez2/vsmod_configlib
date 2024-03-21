using SimpleExpressionEngine;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Datastructures;

namespace ConfigLib;

internal sealed class NumberSettingsContext : IContext<float, float>
{
    private readonly Dictionary<string, float> mSettings;

    public NumberSettingsContext(Dictionary<string, ConfigSetting> settings)
    {
        mSettings = settings.Where(entry => entry.Value.SettingType == ConfigSettingType.Float || entry.Value.SettingType == ConfigSettingType.Integer).ToDictionary(entry => entry.Key, entry => entry.Value.Value.AsFloat(0));
    }

    public bool Resolvable(string name) => mSettings.ContainsKey(name);
    public float Resolve(string name, params float[] arguments) => mSettings[name];
}

internal sealed class BooleanSettingsContext : IContext<bool, bool>
{
    private readonly Dictionary<string, bool> mSettings;

    public BooleanSettingsContext(Dictionary<string, ConfigSetting> settings)
    {
        mSettings = settings.Where(entry => entry.Value.SettingType == ConfigSettingType.Boolean).ToDictionary(entry => entry.Key, entry => entry.Value.Value.AsBool());
    }

    public bool Resolvable(string name) => mSettings.ContainsKey(name);
    public bool Resolve(string name, params bool[] arguments) => mSettings[name];
}

internal sealed class StringSettingsContext : IContext<string, string>
{
    private readonly Dictionary<string, string> mSettings;

    public StringSettingsContext(Dictionary<string, ConfigSetting> settings)
    {
        mSettings = settings.Where(entry => entry.Value.SettingType == ConfigSettingType.String).ToDictionary(entry => entry.Key, entry => entry.Value.Value.AsString());
    }

    public bool Resolvable(string name) => mSettings.ContainsKey(name);
    public string Resolve(string name, params string[] arguments) => mSettings[name];
}

internal sealed class JsonSettingsContext : IContext<JsonObject, JsonObject>
{
    private readonly Dictionary<string, JsonObject> mSettings;

    public JsonSettingsContext(Dictionary<string, ConfigSetting> settings)
    {
        mSettings = settings.Where(entry => entry.Value.SettingType == ConfigSettingType.Other).ToDictionary(entry => entry.Key, entry => entry.Value.Value);
    }

    public bool Resolvable(string name) => mSettings.ContainsKey(name);
    public JsonObject Resolve(string name, params JsonObject[] arguments) => mSettings[name];
}