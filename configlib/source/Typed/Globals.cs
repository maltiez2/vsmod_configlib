namespace configlib.source.Typed;

public static class Globals
{
    public const string ConfigTreeKey = "configlib_configs";
    public const string FileExtension_Json = ".json";
    public const string Unknown = "unknown";
}

public static class Events
{
    public const string InitialConfigLoad = "configlib:InitialConfigLoad";
    public const string ConfigPathDirty = "configlib:ConfigPathDirty";
}
