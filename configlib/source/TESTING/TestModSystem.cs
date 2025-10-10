using ConfigLib;
using Vintagestory.API.Common;

namespace configlib.source.TESTING;
public class TestModSystem : ModSystem
{

    public override void StartPre(ICoreAPI api)
    {
        base.StartPre(api);
    }
    public override void Start(ICoreAPI api)
    {
        base.Start(api);

        var configlib = api.ModLoader.GetModSystem<ConfigLibModSystem>();

    }
}


public class TestConfig
{
    /// <summary>
    /// Just some random field for testing purposes
    /// </summary>
    public string Name { get; set; } = "Testing 101";

    /// <summary>
    /// This describes my intent for the class
    /// </summary>
    public string Description { get; set; } = "A test config for testing purposes";

    /// <summary>
    /// The version of this config
    /// </summary>
    public int Version { get; set; } = 1;
}