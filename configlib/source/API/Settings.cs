using ConfigLib.Formatting;
using Vintagestory.API.Datastructures;

namespace ConfigLib;

//TODO ISetting<T>

public interface ISetting : IConfigBlock
{
    JsonObject Value { get; set; }
    /// <summary>
    /// Sets <see cref="Value"/> to corresponding value from mapping on being set.
    /// </summary>
    string? MappingKey { get; set; }
    JsonObject DefaultValue { get; }
    EnumConfigSettingType SettingType { get; }
    string YamlCode { get; }
    Validation? Validation { get; }
    bool AssignSettingValue(object target);
}

public enum EnumConfigSettingType
{
    /// <summary>
    /// If type is undefined due to some error
    /// </summary>
    None,
    /// <summary>
    /// Corresponds to <see cref="bool"/> values. Edited by checkbox in GUI.
    /// </summary>
    Boolean,
    /// <summary>
    /// Corresponds to <see cref="float"/> values. Edited by slider or entering values directly in GUI. Can have minimum and maximum values specified.
    /// </summary>
    Float,
    /// <summary>
    /// Corresponds to <see cref="int"/> values. Edited by slider or entering values directly in GUI. Can have minimum and maximum values specified.
    /// </summary>
    Integer,
    /// <summary>
    /// Corresponds to <see cref="string"/> values. Edited by line edit in GUI.
    /// </summary>
    String,
    /// <summary>
    /// Color as string in hex format with '#' prefix
    /// </summary>
    Color,
    /// <summary>
    /// Corresponds to arbitrary <see cref="JsonObject"/> values.
    /// </summary>
    Other,
    /// <summary>
    /// Hidden setting that just stores constant value for use in patches
    /// </summary>
    Constant
    
}