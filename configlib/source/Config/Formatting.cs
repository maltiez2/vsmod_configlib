using ImGuiNET;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace ConfigLib.Formatting;

public interface IConfigBlock
{
}

internal interface IFormattingBlock : IConfigBlock
{
    float SortingWeight { get; }
    public bool Collapsible { get; }
    public bool StopCollapsible { get; }
    string Yaml { get; }
    bool Draw(string id);
}

internal sealed class Blank : IFormattingBlock
{
    public float SortingWeight => 0;
    public bool Collapsible => false;
    public bool StopCollapsible => false;
    public string Yaml => "";

    public bool Draw(string id)
    {
        return true;
    }
}

internal sealed class Separator : IFormattingBlock
{
    public Separator(JsonObject definition, string domain, ICoreAPI api)
    {
        _weight = definition["weight"].AsFloat(0);
        _collapsible = definition["collapsible"].AsBool(false);
        _stopCollapsible = _collapsible;
        _weight = _weight < 0 ? 0 : _weight;
        StringBuilder yaml = new();
        yaml.Append("\n\n");

        if (definition.KeyExists("title"))
        {
            _stopCollapsible = true;
            string title = Localize(definition["title"].AsString(), domain);
            _title = title;
            int width = title.Length + 6;
            string line = new('#', width);
            yaml.Append($"{line}\n## {title} ##\n{line}\n");
        }

        if (definition.KeyExists("text"))
        {
            string text = Localize(definition["text"].AsString(), domain);
            _text = text;
            string[] lines = text.Split('\n');
            string composed = lines.Select(line => $"# {line}").Aggregate((first, second) => $"{first}\n{second}");
            yaml.Append($"{composed}\n");
        }

        if (definition.KeyExists("link"))
        {
            _link = definition["link"].AsString();
            _linkText = definition["linkText"].AsString(null);
            if (_linkText != null) _linkText = Localize(_linkText, domain);
            yaml.Append($"# {_link}\n");
        }

        _yaml = yaml.ToString();
        _api = api;
    }

    public string Yaml => _yaml;
    public float SortingWeight => _weight;
    public bool Collapsible => _collapsible;
    public bool StopCollapsible => _stopCollapsible;

    public bool Draw(string id)
    {
        bool collapsed = false;

        if (_title != null)
        {
            if (_collapsible)
            {
                collapsed = !ImGuiNET.ImGui.CollapsingHeader($"{_title}##{id}");
            }
            else
            {
                ImGuiNET.ImGui.SeparatorText(_title);
            }
        }
        else
        {
            ImGuiNET.ImGui.Separator();
        }

        if (_text != null)
        {
            ImGuiNET.ImGui.TextWrapped(_text);
        }

        if (_link != null)
        {
            string linkText = _linkText ?? _link;

            if (ImGui.Button($"{linkText}##{id}"))
            {
                (_api as ICoreClientAPI)?.Gui.OpenLink(_link);
            }
            if (ImGui.BeginItemTooltip())
            {
                ImGui.PushTextWrapPos(ImGui.GetFontSize() * 35f);
                ImGui.TextUnformatted($"open in browser: {_link}");
                ImGui.PopTextWrapPos();

                ImGui.EndTooltip();
            }
            ImGuiNET.ImGui.Separator();
        }

        return !collapsed;
    }


    private readonly string _yaml;
    private readonly float _weight;
    private readonly bool _collapsible;
    private readonly bool _stopCollapsible;
    private readonly string? _title;
    private readonly string? _text;
    private readonly string? _link;
    private readonly string? _linkText;
    private readonly ICoreAPI _api;

    private static string Localize(string value, string domain)
    {
        bool hasDomain = value.Contains(':');
        string langCode = hasDomain ? value : $"{domain}:{value}";
        return Lang.HasTranslation(langCode) ? Lang.Get(langCode) : value;
    }
}