using Newtonsoft.Json.Linq;
using SimpleExpressionEngine;
using System.Text.RegularExpressions;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.Common;
using YamlDotNet.Core.Tokens;

namespace ConfigLib;

public class ConfigPatches
{
    private readonly ICoreAPI _api;
    private readonly List<AssetPatch> _patches = new();

    public ConfigPatches(ICoreAPI api, Config config)
    {
        _api = api;

        HashSet<string> files = GetFiles(config.Definition["patches"]);

        foreach (string filePath in files)
        {
            try
            {
                _patches.Add(new(api, filePath, config));
            }
            catch (Exception exception)
            {
                _api.Logger.Debug($"[Config lib] Exception on creating patch to asset: '{filePath}'.");
                _api.Logger.VerboseDebug($"[Config lib] Exception on creating patch to asset:\n{exception}\n.");
            }
        }
    }

    public void Apply()
    {
        foreach (AssetPatch patch in _patches)
        {
            try
            {
                (int successful, int failed) = patch.Apply(out bool serverSide);
                if (!serverSide && successful >= 0) _api.Logger.Debug($"[Config lib] Values patched: {successful}, failed: {failed} in asset: '{patch.File}'.");
            }
            catch (Exception exception)
            {
                _api.Logger.Debug($"[Config lib] Exception on applying patch to asset: '{patch.File}'.");
                _api.Logger.VerboseDebug($"[Config lib] Exception on applying patch to asset:\n{exception}\n.");
            }
        }
    }

    public static HashSet<string> GetFiles(JsonObject? definition)
    {
        HashSet<string> files = new();

        if (definition?.Token is not JObject categories) return files;

        foreach ((_, JToken? category) in categories)
        {
            if (category is not JObject categoryObject) continue;

            foreach ((string file, _) in categoryObject)
            {
                if (!files.Contains(file)) files.Add(file);
            }
        }

        return files;
    }
}

internal partial class AssetPatch
{
    public string File => _asset;

    private readonly string _asset;
    private readonly List<IValuePatch> _patches = new();
    private readonly ICoreAPI _api;
    private readonly HashSet<AssetCategory> _serverSideCategories = new()
    {
        AssetCategory.itemtypes,
        AssetCategory.blocktypes,
        AssetCategory.entities,
        AssetCategory.recipes
    };

    public AssetPatch(ICoreAPI api, string assetPath, Config config)
    {
        _api = api;
        _asset = assetPath;

        JsonObject definition = config.Definition["patches"];
        CombinedContext<float, float> context = new(new List<IContext<float, float>>() { new MathContext(), new BooleanMathContext(), new NumberSettingsContext(config.Settings) });

        if (!ConstructBooleanPatches(definition, assetPath, context))
            _api.Logger.Debug($"[Config lib] Error on parsing 'boolean' patches for '{assetPath}'.");
        if (!ConstructNumberPatches(definition, assetPath, context, "number", ConfigSettingType.Float))
            _api.Logger.Debug($"[Config lib] Error on parsing 'number' patches for '{assetPath}'.");
        if (!ConstructNumberPatches(definition, assetPath, context, "float", ConfigSettingType.Float))
            _api.Logger.Debug($"[Config lib] Error on parsing 'float' patches for '{assetPath}'.");
        if (!ConstructNumberPatches(definition, assetPath, context, "integer", ConfigSettingType.Integer))
            _api.Logger.Debug($"[Config lib] Error on parsing 'integer' patches for '{assetPath}'.");
        if (!ConstructConstPatches(definition, assetPath))
            _api.Logger.Debug($"[Config lib] Error on parsing 'const' patches for '{assetPath}'.");
        if (!ConstructStringPatches(definition, assetPath, config, context))
            _api.Logger.Debug($"[Config lib] Error on parsing 'string' patches for '{assetPath}'.");
        if (!ConstructJsonPatches(definition, assetPath, config, context))
            _api.Logger.Debug($"[Config lib] Error on parsing 'other' patches for '{assetPath}'.");
    }

    public (int successful, int failed) Apply(out bool serverSideAsset)
    {
        JsonObject? asset = RetrieveAsset(out serverSideAsset);
        if (serverSideAsset) return (0, 0);
        if (asset == null) return (-1, -1);

        int count = 0;

        foreach (IValuePatch patch in _patches)
        {
            try
            {
                patch.Apply(asset);
            }
            catch (Exception exception)
            {
                _api.Logger.VerboseDebug($"[Config lib] Failed to apply patch to '{_asset}' asset.\nException: {exception}\n");
                count++;
            }
        }

        StoreAsset(asset);

        return (_patches.Count - count, count);
    }

    private JsonObject? RetrieveAsset(out bool serverSide)
    {
        serverSide = false;

        AssetLocation location = new(_asset);
        if (_api.Side == EnumAppSide.Client && _serverSideCategories.Contains(location.Category))
        {
            serverSide = true;
            return null;
        }

        IAsset? asset;
        try
        {
            asset = _api.Assets.Get(_asset);
        }
        catch
        {
            _api.Logger.Debug($"[Config lib] Asset '{_asset}' not found, skipping it.");
            return null;
        }

        if (asset == null) return null;



        string json = Asset.BytesToString(asset.Data);

        try
        {
            return new(JArray.Parse(json));
        }
        catch
        {
            try
            {
                return new(JObject.Parse(json));
            }
            catch
            {
                return null;
            }
        }
    }

    private void StoreAsset(JsonObject data)
    {
        IAsset? asset = _api.Assets.Get(_asset);
        if (asset == null) return;
        asset.Data = System.Text.Encoding.UTF8.GetBytes(data.ToString());
    }

    private static readonly Regex _booleanExpressionRegex = GetBooleanExpressionRegex();

    private bool ConstructBooleanPatches(JsonObject definition, string assetPath, CombinedContext<float, float> context)
    {
        if (!definition.KeyExists("boolean")) return true;
        if (!definition["boolean"].KeyExists(assetPath)) return true;
        if (definition["boolean"][assetPath]?.Token is not JObject booleanPatches) return false;

        bool failed = false;

        foreach ((string key, JToken? value) in booleanPatches)
        {
            if (value is not JValue boolValue || boolValue.Type != JTokenType.String)
            {
                _api.Logger.Error($"[Config lib] Error on parsing patch '{assetPath}/{key}': patch value '{value}' is not string.");
                failed = true;
                continue;
            }
            string setting = (string?)boolValue.Value ?? "";

            setting = Process(setting, context);

            _patches.Add(new BooleanPatch(key, setting, context));
        }

        return !failed;
    }
    private bool ConstructStringPatches(JsonObject definition, string assetPath, Config config, CombinedContext<float, float> context)
    {
        if (!definition.KeyExists("string")) return true;
        if (!definition["string"].KeyExists(assetPath)) return true;
        if (definition["string"][assetPath]?.Token is not JObject stringPatches) return false;

        bool failed = false;

        foreach ((string key, JToken? value) in stringPatches)
        {
            if (value is not JValue boolValue || boolValue.Type != JTokenType.String)
            {
                _api.Logger.Error($"[Config lib] Error on parsing patch '{assetPath}/{key}': patch value '{value}' is not string.");
                failed = true;
                continue;
            }
            
            string setting = (string?)boolValue.Value ?? "";
            setting = Process(setting, context);

            if (config.GetSetting(setting) == null)
            {
                _api.Logger.Error($"[Config lib] Error on parsing patch '{assetPath}/{key}': other setting '{setting}' not found.");
                failed = true;
                continue;
            }
            if (config.GetSetting(setting)?.SettingType != ConfigSettingType.String)
            {
                _api.Logger.Error($"[Config lib] Error on parsing patch '{assetPath}/{key}': setting '{setting}' is not from 'string' category.");
                failed = true;
                continue;
            }
            _patches.Add(new StringPatch(key, setting, config));
        }

        return !failed;
    }
    private bool ConstructJsonPatches(JsonObject definition, string assetPath, Config config, CombinedContext<float, float> context)
    {
        if (!definition.KeyExists("other")) return true;
        if (!definition["other"].KeyExists(assetPath)) return true;
        if (definition["other"][assetPath]?.Token is not JObject jsonPatches) return false;

        bool failed = false;

        foreach ((string key, JToken? value) in jsonPatches)
        {
            if (value is not JValue boolValue || boolValue.Type != JTokenType.String)
            {
                _api.Logger.Error($"[Config lib] Error on parsing patch '{assetPath}/{key}': patch value '{value}' is not string.");
                failed = true;
                continue;
            }

            string setting = (string?)boolValue.Value ?? "";
            setting = Process(setting, context);
            
            if (config.GetSetting(setting) == null)
            {
                _api.Logger.Error($"[Config lib] Error on parsing patch '{assetPath}/{key}': other setting '{setting}' not found.");
                failed = true;
                continue;
            }
            if (config.GetSetting(setting)?.SettingType != ConfigSettingType.Other)
            {
                _api.Logger.Error($"[Config lib] Error on parsing patch '{assetPath}/{key}': setting '{setting}' is not form 'other' category.");
                failed = true;
                continue;
            }
            _patches.Add(new JsonPatch(key, setting, config));
        }

        return !failed;
    }
    private bool ConstructNumberPatches(JsonObject definition, string assetPath, CombinedContext<float, float> context, string category, ConfigSettingType type)
    {
        if (!definition.KeyExists(category)) return true;
        if (!definition[category].KeyExists(assetPath)) return true;
        if (definition[category][assetPath]?.Token is not JObject jsonPatches) return false;

        bool failed = false;

        foreach ((string key, JToken? value) in jsonPatches)
        {
            if (value is not JValue formula || formula.Type != JTokenType.String)
            {
                _api.Logger.Error($"[Config lib] Error on parsing '{category}' patch '{assetPath}/{key}': patch value '{value}' is not string.");
                failed = true;
                continue;
            }
            string setting = (string?)formula.Value ?? "";
            setting = Process(setting, context);

            _patches.Add(new NumberPatch(key, setting, context, type));
        }

        return !failed;
    }
    private bool ConstructConstPatches(JsonObject definition, string assetPath)
    {
        if (!definition.KeyExists("const")) return true;
        if (!definition["const"].KeyExists(assetPath)) return true;
        if (definition["const"][assetPath]?.Token is not JObject jsonPatches) return false;

        bool failed = false;

        foreach ((string key, JToken? value) in jsonPatches)
        {
            _patches.Add(new ConstPatch(key, new JsonObject(value)));
        }

        return !failed;
    }

    [GeneratedRegex("\\((?<expression>.+)\\) \\? (?<true>.+) \\: (?<false>.+)", RegexOptions.Compiled)]
    private static partial Regex GetBooleanExpressionRegex();

    private const float _epsilon = 1e-10f;
    private string Process(string value, IContext<float, float> context)
    {
        Match match = _booleanExpressionRegex.Match(value);

        if (!match.Success) return value;

        string expression = match.Groups["expression"].Value;
        string onTrue = match.Groups["true"].Value;
        string onFalse = match.Groups["false"].Value;

        float result = MathParser.Parse(expression).Evaluate(context);

        return Math.Abs(result) > _epsilon ? onTrue : onFalse;
    }
}

internal interface IValuePatch
{
    void Apply(JsonObject asset);
}

internal sealed class BooleanExpressionSelector
{
    private readonly INode<float, float, float> _value;
    private readonly IContext<float, float> _context;
    private readonly string _onTrue;
    private readonly string _onFalse;
    private const float _epsilon = 1e-10f;

    public BooleanExpressionSelector(string expression, string onTrue, string onFalse, IContext<float, float> context)
    {
        _value = MathParser.Parse(expression);
        _context = context;
        _onTrue = onTrue;
        _onFalse = onFalse;
    }

    public string Resolve(float value)
    {
        CombinedContext<float, float> context = new(new List<IContext<float, float>> { _context, new ValueContext<float, float>(value) });

        float result = _value.Evaluate(context);

        return Math.Abs(result) > _epsilon ? _onTrue : _onFalse;
    }
}

internal sealed class NumberPatch : IValuePatch
{
    private readonly JsonObjectPath _path;
    private readonly INode<float, float, float> _value;
    private readonly IContext<float, float> _context;
    private readonly ConfigSettingType _returnType;

    public NumberPatch(string path, string formula, IContext<float, float> context, ConfigSettingType type)
    {
        _path = new(path);
        _value = MathParser.Parse(formula);
        _context = context;
        _returnType = type;
    }

    public void Apply(JsonObject asset)
    {
        JsonObject? jsonValue = _path.Get(asset);

        float previousValue = jsonValue?.AsFloat(0) ?? 0;

        if (jsonValue is not null && ((JValue)jsonValue.Token).Value is bool)
        {
            previousValue = jsonValue.AsBool() ? 1 : 0;
        }

        CombinedContext<float, float> context = new(new List<IContext<float, float>> { new ValueContext<float, float>(previousValue), _context });

        float value = _value.Evaluate(context);
        
        switch (_returnType)
        {
            case ConfigSettingType.Float:
                jsonValue?.Token.Replace(new JValue(value));
                break;
            case ConfigSettingType.Integer:
                jsonValue?.Token.Replace(new JValue((int)value));
                break;
        }
    }
}
internal sealed class BooleanPatch : IValuePatch
{
    private readonly JsonObjectPath _path;
    private readonly INode<float, float, float> _value;
    private readonly IContext<float, float> _context;
    private const float _epsilon = 1e-10f;

    public BooleanPatch(string path, string formula, IContext<float, float> context)
    {
        _path = new(path);
        _value = MathParser.Parse(formula);
        _context = context;
    }

    public void Apply(JsonObject asset)
    {
        JsonObject? jsonValue = _path.Get(asset);

        float previousValue = (jsonValue?.AsBool() ?? false) ? 1 : 0;

        CombinedContext<float, float> context = new(new List<IContext<float, float>> { _context, new ValueContext<float, float>(previousValue) });

        float value = _value.Evaluate(context);
        bool result = Math.Abs(value) > _epsilon;

        jsonValue?.Token.Replace(new JValue(result));
    }
}
internal sealed class StringPatch : IValuePatch
{
    private readonly JsonObjectPath mPath;
    private readonly string mValue;

    public StringPatch(string path, string value, Config config)
    {
        mPath = new(path);
        mValue = config.GetSetting(value)?.Value.AsString() ?? "";
    }

    public void Apply(JsonObject asset)
    {
        JsonObject? jsonValue = mPath.Get(asset);
        jsonValue?.Token.Replace(new JValue(mValue));
    }
}
internal sealed class JsonPatch : IValuePatch
{
    private readonly JsonObjectPath mPath;
    private readonly JsonObject? mValue;

    public JsonPatch(string path, string value, Config config)
    {
        mPath = new(path);
        mValue = config.GetSetting(value)?.Value;
    }

    public void Apply(JsonObject asset)
    {
        if (mValue == null) return;
        JsonObject? jsonValue = mPath.Get(asset);
        jsonValue?.Token.Replace(mValue.Token);
    }
}
internal sealed class ConstPatch : IValuePatch
{
    private readonly JsonObjectPath mPath;
    private readonly JsonObject mValue;

    public ConstPatch(string path, JsonObject value)
    {
        mPath = new(path);
        mValue = value;
    }

    public void Apply(JsonObject asset)
    {
        if (mValue == null) return;
        JsonObject? jsonValue = mPath.Get(asset);
        jsonValue?.Token.Replace(mValue.Token);
    }
}

internal sealed class JsonObjectPath
{
    private delegate JsonObject? PathElement(JsonObject? attribute);
    private readonly IEnumerable<PathElement> mPath;

    public JsonObjectPath(string path)
    {
        mPath = path.Split("/").Where(element => element != "").Select(Convert);
    }

    private PathElement Convert(string element)
    {
        if (int.TryParse(element, out int index))
        {
            return tree => PathElementByIndex(tree, index);
        }
        else
        {
            if (element == "-") return tree => PathElementByIndex(tree, -1);

            return tree => PathElementByKey(tree, element);
        }
    }

    public JsonObject? Get(JsonObject? tree)
    {
        JsonObject? result = tree;
        foreach (PathElement element in mPath)
        {
            result = element.Invoke(result);
            if (result == null) return null;
        }
        return result;
    }

    public bool Set(JsonObject? tree, JsonObject value)
    {
        JsonObject? result = Get(tree);
        if (result == null) return false;
        result.Token?.Replace(value.Token);
        return true;
    }

    private static JsonObject? PathElementByIndex(JsonObject? attribute, int index)
    {
        if (attribute?.IsArray() != true) return null;

        if (index == -1 || attribute.AsArray().Length <= index)
        {
            (attribute.Token as JArray)?.Add(new JValue(0));
            index = ((attribute.Token as JArray)?.Count ?? 1) - 1;
        }

        JsonObject[] jsonArray = attribute.AsArray();

        return jsonArray[index];
    }
    private static JsonObject? PathElementByKey(JsonObject? attribute, string key)
    {
        if (attribute?.KeyExists(key) == true) return attribute[key];

        if (attribute?.Token is not JObject token) return null;

        token.Add(key, new JValue(0));

        return attribute[key];
    }
}