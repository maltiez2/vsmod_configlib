using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace ConfigLib;

public class JsonConfigsSystem : ModSystem
{
    public const string ConfigSavedEvent = "{0}:saved";
    public const string ConfigChangedEvent = "{0}:changed";

    public override void StartClientSide(ICoreClientAPI api)
    {
        _api = api;
        api.Event.RegisterEventBusListener(ReloadJsonConfigs, filterByEventName: "configlib:reload");
    }

    public void OnConfigSaved(string domain, string file)
    {
        TreeAttribute eventDataTree = new();
        eventDataTree.SetString("domain", domain);
        eventDataTree.SetString("file", file);
        _api?.Event.PushEvent(string.Format(ConfigSavedEvent, domain), eventDataTree);
    }
    public void OnSettingChanged(string domain, string file, ConfigSetting setting)
    {
        TreeAttribute eventDataTree = new();
        eventDataTree.SetString("domain", domain);
        eventDataTree.SetString("file", file);
        eventDataTree.SetString("setting", setting.YamlCode);
        
        switch (setting.SettingType)
        {
            case ConfigSettingType.Boolean:
                eventDataTree.SetBool("value", setting.Value.AsBool());
                break;
            case ConfigSettingType.Float:
                eventDataTree.SetFloat("value", setting.Value.AsFloat());
                break;
            case ConfigSettingType.Integer:
                eventDataTree.SetInt("value", setting.Value.AsInt());
                break;
            case ConfigSettingType.String:
                eventDataTree.SetString("value", setting.Value.AsString());
                break;
            case ConfigSettingType.Other:
                eventDataTree.SetAttribute("value", setting.Value.ToAttribute());
                break;
        }
        _api?.Event.PushEvent(string.Format(ConfigChangedEvent, domain), eventDataTree);
    }

    private ICoreClientAPI? _api;

    private void ReloadJsonConfigs(string eventName, ref EnumHandling handling, IAttribute data)
    {
        string domain = (data as ITreeAttribute)?.GetAsString("domain") ?? "";
        string file = (data as ITreeAttribute)?.GetAsString("domain") ?? "";
    }

}
