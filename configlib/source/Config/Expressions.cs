using SimpleExpressionEngine;
using Vintagestory.API.Datastructures;

namespace ConfigLib;

internal sealed class NumberSettingsContext : IContext<float, float>
{
    private readonly Dictionary<string, float> _settings;

    public NumberSettingsContext(Dictionary<string, ConfigSetting> settings)
    {
        _settings = settings.Where(entry => Supported(entry.Value)).ToDictionary(entry => entry.Key, entry => ConvertSetting(entry.Value));
    }

    public bool Resolvable(string name) => _settings.ContainsKey(name);
    public float Resolve(string name, params float[] arguments) => _settings[name];

    private static bool Supported(ConfigSetting setting)
    {
        return setting.SettingType switch
        {
            ConfigSettingType.Boolean => true,
            ConfigSettingType.Float => true,
            ConfigSettingType.Integer => true,
            _ => false
        };
    }
    private static float ConvertSetting(ConfigSetting setting)
    {
        return setting.SettingType switch
        {
            ConfigSettingType.Boolean => setting.Value.AsBool() ? 1 : 0,
            ConfigSettingType.Float => setting.Value.AsFloat(),
            ConfigSettingType.Integer => setting.Value.AsFloat(),
            _ => 0
        };
    }
}

internal sealed class BooleanSettingsContext : IContext<bool, bool>
{
    private readonly Dictionary<string, bool> _settings;

    public BooleanSettingsContext(Dictionary<string, ConfigSetting> settings)
    {
        _settings = settings.Where(entry => entry.Value.SettingType == ConfigSettingType.Boolean).ToDictionary(entry => entry.Key, entry => entry.Value.Value.AsBool());
    }

    public bool Resolvable(string name) => _settings.ContainsKey(name);
    public bool Resolve(string name, params bool[] arguments) => _settings[name];
}

internal sealed class StringSettingsContext : IContext<string, string>
{
    private readonly Dictionary<string, string> _settings;

    public StringSettingsContext(Dictionary<string, ConfigSetting> settings)
    {
        _settings = settings.Where(entry => entry.Value.SettingType == ConfigSettingType.String).ToDictionary(entry => entry.Key, entry => entry.Value.Value.AsString());
    }

    public bool Resolvable(string name) => _settings.ContainsKey(name);
    public string Resolve(string name, params string[] arguments) => _settings[name];
}

internal sealed class JsonSettingsContext : IContext<JsonObject, JsonObject>
{
    private readonly Dictionary<string, JsonObject> _settings;

    public JsonSettingsContext(Dictionary<string, ConfigSetting> settings)
    {
        _settings = settings.Where(entry => entry.Value.SettingType == ConfigSettingType.Other).ToDictionary(entry => entry.Key, entry => entry.Value.Value);
    }

    public bool Resolvable(string name) => _settings.ContainsKey(name);
    public JsonObject Resolve(string name, params JsonObject[] arguments) => _settings[name];
}

internal sealed class ValueContext<TResult, TArguments> : IContext<TResult, TArguments>
{
    private readonly TResult _value;

    public ValueContext(TResult value)
    {
        _value = value;
    }

    public bool Resolvable(string name)
    {
        return name == "value";
    }
    public TResult Resolve(string name, params TArguments[] arguments)
    {
        if (name == "value") return _value;

        throw new InvalidDataException($"Unresolvable: '{name}'");
    }
}

internal sealed class BooleanMathContext : IContext<float, float>
{
    private const float _epsilon = 1E-10f;

    public BooleanMathContext()
    {
    }

    public bool Resolvable(string name)
    {
        return name switch
        {
            "if" => true,
            "not" => true,
            "and" => true,
            "or" => true,
            "true" => true,
            "false" => true,
            _ => false
        };
    }

    public float Resolve(string name, params float[] arguments)
    {
        return name switch
        {
            "if" => AsBool(arguments[0]) ? arguments[1] : arguments[2],
            "not" => AsFloat(!AsBool(arguments[0])),
            "and" => AsFloat(AsBool(arguments[0]) && AsBool(arguments[1])),
            "or" => AsFloat(AsBool(arguments[0]) || AsBool(arguments[1])),
            "true" => _true,
            "false" => _false,
            _ => throw new InvalidDataException($"Unknown function: '{name}'")
        };
    }

    private static bool AsBool(float value) => MathF.Abs(value) > _epsilon;
    private static float AsFloat(bool value) => value ? _true : _false;
    
    private const float _true = 1;
    private const float _false = 0;
}