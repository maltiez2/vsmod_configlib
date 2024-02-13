using HarmonyLib;
using Newtonsoft.Json.Linq;
using SimpleExpressionEngine;
using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.Common;

namespace ConfigLib;

public class ConfigPatches
{
    private readonly ICoreAPI mApi;
    private readonly List<AssetPatch> mPatches = new();

    public ConfigPatches(ICoreAPI api, Config config)
    {
        mApi = api;

        HashSet<string> files = GetFiles(config.Definition["patches"]);

        foreach (string filePath in files)
        {
            try
            {
                mPatches.Add(new(api, filePath, config));
            }
            catch (Exception exception)
            {
                mApi.Logger.Debug($"[Config lib] Exception on creating patch to asset: '{filePath}'.");
                mApi.Logger.VerboseDebug($"[Config lib] Exception on creating patch to asset:\n{exception}\n.");
            }
        }
    }

    public void Apply()
    {
        foreach (AssetPatch patch in mPatches)
        {
            try
            {
                (int successful, int failed) = patch.Apply();
                if (successful >= 0) mApi.Logger.Debug($"[Config lib] Values patched: {successful}, failed: {failed} in asset: '{patch.File}'.");
            }
            catch (Exception exception)
            {
                mApi.Logger.Debug($"[Config lib] Exception on applying patch to asset: '{patch.File}'.");
                mApi.Logger.VerboseDebug($"[Config lib] Exception on applying patch to asset:\n{exception}\n.");
            }
        }
    }

    public HashSet<string> GetFiles(JsonObject definition)
    {
        HashSet<string> files = new();

        if (definition.Token is not JObject categories) return files;

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

internal class AssetPatch
{
    public string File => mAsset;

    private readonly string mAsset;
    private readonly List<IValuePatch> mPatches = new();
    private readonly ICoreAPI mApi;

    public AssetPatch(ICoreAPI api, string assetPath, Config config)
    {
        mApi = api;
        mAsset = assetPath;

        JsonObject definition = config.Definition["patches"];
        CombinedContext<float, float> context = new(new List<IContext<float, float>>() { new MathContext(), new NumberSettingsContext(config.Settings) });

        if (!ConstructBooleanPatches(definition, assetPath, config))
        {
            mApi.Logger.Debug($"[Config lib] Error on parsing 'boolean' patches for '{assetPath}'.");
        }
        if (!ConstructNumberPatches(definition, assetPath, context, "number", ConfigSettingType.Float))
        {
            mApi.Logger.Debug($"[Config lib] Error on parsing 'number' patches for '{assetPath}'.");
        }
        if (!ConstructNumberPatches(definition, assetPath, context, "float", ConfigSettingType.Float))
        {
            mApi.Logger.Debug($"[Config lib] Error on parsing 'float' patches for '{assetPath}'.");
        }
        if (!ConstructNumberPatches(definition, assetPath, context, "integer", ConfigSettingType.Integer))
        {
            mApi.Logger.Debug($"[Config lib] Error on parsing 'integer' patches for '{assetPath}'.");
        }
        if (!ConstructConstPatches(definition, assetPath))
        {
            mApi.Logger.Debug($"[Config lib] Error on parsing 'const' patches for '{assetPath}'.");
        }
        if (!ConstructJsonPatches(definition, assetPath, config))
        {
            mApi.Logger.Debug($"[Config lib] Error on parsing 'other' patches for '{assetPath}'.");
        }
    }

    public (int successful, int failed) Apply()
    {
        JsonObject? asset = RetrieveAsset();
        if (asset == null) return (-1, -1);

        int count = 0;

        foreach (IValuePatch patch in mPatches)
        {
            try
            {
                patch.Apply(asset);
            }
            catch (Exception exception)
            {
                mApi.Logger.VerboseDebug($"[Config lib] Failed to apply patch to '{mAsset}' asset.\nException: {exception}\n");
                count++;
            }
        }

        StoreAsset(asset);

        return (mPatches.Count - count, count);
    }

    private JsonObject? RetrieveAsset()
    {
        IAsset? asset;
        try
        {
            asset = mApi.Assets.Get(mAsset);
        }
        catch
        {
            mApi.Logger.Debug($"[Config lib] Asset '{mAsset}' not found, skipping it.");
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
        IAsset? asset = mApi.Assets.Get(mAsset);
        if (asset == null) return;
        asset.Data = System.Text.Encoding.UTF8.GetBytes(data.ToString());
    }

    private bool ConstructBooleanPatches(JsonObject definition, string assetPath, Config config)
    {
        if (!definition.KeyExists("boolean")) return true;
        if (!definition["boolean"].KeyExists(assetPath)) return true;
        if (definition["boolean"][assetPath]?.Token is not JObject booleanPatches) return false;

        bool failed = false;

        foreach ((string key, JToken? value) in booleanPatches)
        {
            if (value is not JValue boolValue || boolValue.Type != JTokenType.String)
            {
                mApi.Logger.Error($"[Config lib] Error on parsing patch '{assetPath}/{key}': patch value '{value}' is not string.");
                failed = true;
                continue;
            }
            string setting = (string?)boolValue.Value ?? "";
            if (config.GetSetting(setting) == null)
            {
                mApi.Logger.Error($"[Config lib] Error on parsing patch '{assetPath}/{key}': bool setting '{setting}' not found.");
                failed = true;
                continue;
            }
            if (config.GetSetting(setting)?.SettingType != ConfigSettingType.Boolean)
            {
                mApi.Logger.Error($"[Config lib] Error on parsing patch '{assetPath}/{key}': setting '{setting}' is not form 'boolean' category.");
                failed = true;
                continue;
            }
            mPatches.Add(new BooleanPatch(key, setting, config));
        }

        return !failed;
    }
    private bool ConstructJsonPatches(JsonObject definition, string assetPath, Config config)
    {
        if (!definition.KeyExists("other")) return true;
        if (!definition["other"].KeyExists(assetPath)) return true;
        if (definition["other"][assetPath]?.Token is not JObject jsonPatches) return false;

        bool failed = false;

        foreach ((string key, JToken? value) in jsonPatches)
        {
            if (value is not JValue boolValue || boolValue.Type != JTokenType.String)
            {
                mApi.Logger.Error($"[Config lib] Error on parsing patch '{assetPath}/{key}': patch value '{value}' is not string.");
                failed = true;
                continue;
            }
            string setting = (string?)boolValue.Value ?? "";
            if (config.GetSetting(setting) == null)
            {
                mApi.Logger.Error($"[Config lib] Error on parsing patch '{assetPath}/{key}': other setting '{setting}' not found.");
                failed = true;
                continue;
            }
            if (config.GetSetting(setting)?.SettingType != ConfigSettingType.Other)
            {
                mApi.Logger.Error($"[Config lib] Error on parsing patch '{assetPath}/{key}': setting '{setting}' is not form 'other' category.");
                failed = true;
                continue;
            }
            mPatches.Add(new JsonPatch(key, setting, config));
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
            if (value is not JValue boolValue || boolValue.Type != JTokenType.String)
            {
                mApi.Logger.Error($"[Config lib] Error on parsing '{category}' patch '{assetPath}/{key}': patch value '{value}' is not string.");
                failed = true;
                continue;
            }
            string setting = (string?)boolValue.Value ?? "";

            mPatches.Add(new NumberPatch(key, setting, context, type));
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
            mPatches.Add(new ConstPatch(key, new JsonObject(value)));
        }

        return !failed;
    }
}

internal interface IValuePatch
{
    void Apply(JsonObject asset);
}

internal sealed class NumberPatch : IValuePatch
{
    private readonly JsonObjectPath mPath;
    private readonly INode<float, float, float> mValue;
    private readonly IContext<float, float> mContext;
    private readonly ConfigSettingType mReturnType;

    public NumberPatch(string path, string formula, IContext<float, float> context, ConfigSettingType type)
    {
        mPath = new(path);
        mValue = MathParser.Parse(formula);
        mContext = context;
        mReturnType = type;
    }

    public void Apply(JsonObject asset)
    {
        float value = mValue.Evaluate(mContext);
        JsonObject? jsonValue = mPath.Get(asset);
        switch (mReturnType)
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
    private readonly JsonObjectPath mPath;
    private readonly bool mValue;

    public BooleanPatch(string path, string value, Config config)
    {
        mPath = new(path);
        mValue = config.GetSetting(value)?.Value.AsBool(false) ?? false;
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