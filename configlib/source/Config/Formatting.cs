using System.Linq;
using System.Text;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace ConfigLib.Formatting;

public interface IConfigBlock
{
}

internal interface IFormattingBlock : IConfigBlock
{
    float SortingWeight { get; }
    string Yaml { get; }
    void Draw(string id);
}

internal sealed class Blank : IFormattingBlock
{
    public float SortingWeight => 0;

    public string Yaml => "";

    public void Draw(string id)
    {

    }
}

internal sealed class Separator : IFormattingBlock
{
    public Separator(JsonObject definition, string domain)
    {
        _weight = definition["weight"].AsFloat(0);
        _weight = _weight < 0 ? 0 : _weight;
        StringBuilder yaml = new();
        yaml.Append("\n\n");

        if (definition.KeyExists("title"))
        {
            string title = Localize(definition["title"].AsString(), domain);
            _title = title;
            int width = title.Length + 6;
            string line = new string('#', width);
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

        _yaml = yaml.ToString();
    }

    public string Yaml => _yaml;
    public float SortingWeight => _weight;

    public void Draw(string id)
    {
        if (_title != null)
        {
            ImGuiNET.ImGui.SeparatorText(_title);
        }
        else
        {
            ImGuiNET.ImGui.Separator();
        }

        if (_text != null)
        {
            ImGuiNET.ImGui.TextWrapped(_text);
        }
    }


    private readonly string _yaml;
    private readonly float _weight;
    private readonly string? _title;
    private readonly string? _text;

    private static string Localize(string value, string domain)
    {
        bool hasDomain = value.Contains(':');
        string langCode = hasDomain ? value : $"{domain}:{value}";
        return Lang.HasTranslation(langCode) ? Lang.Get(langCode) : value;
    }
}
