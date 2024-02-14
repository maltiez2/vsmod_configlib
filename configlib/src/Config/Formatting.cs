using ImGuiNET;
using System.Linq;
using System.Text;
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
    public Separator(JsonObject definition)
    {
        mWeight = definition["weight"].AsFloat(0);
        mWeight = mWeight < 0 ? 0 : mWeight;
        StringBuilder yaml = new();
        yaml.Append("\n\n");

        if (definition.KeyExists("title"))
        {
            string title = definition["title"].AsString();
            mTitle = title;
            int width = title.Length + 6;
            string line = new string('#', width);
            yaml.Append($"{line}\n## {title} ##\n{line}\n");
        }

        if (definition.KeyExists("text"))
        {
            string text = definition["text"].AsString();
            mText = text;
            string[] lines = text.Split('\n');
            string composed = lines.Select(line => $"# {line}").Aggregate((first, second) => $"{first}\n{second}");
            yaml.Append($"{composed}\n");
        }

        mYaml = yaml.ToString();
    }

    public string Yaml => mYaml;
    public float SortingWeight => mWeight;

    public void Draw(string id)
    {
        if (mTitle != null)
        {
            ImGui.SeparatorText(mTitle);
        }
        else
        {
            ImGui.Separator();
        }

        if (mText != null)
        {
            ImGui.TextWrapped(mText);
        }
    }


    private readonly string mYaml;
    private readonly float mWeight;
    private readonly string? mTitle;
    private readonly string? mText;
}
