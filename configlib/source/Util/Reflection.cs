using System.Reflection;

namespace configlib.source.Util;
internal static class Reflection
{
    // Backing field name pattern: "<PropertyName>k__BackingField"
    public static bool IsBackingField(this MemberInfo field) => field.Name.StartsWith('<') && field.Name.Contains("k__BackingField");

    internal static bool CanGetValue(this MemberInfo memberInfo)
    {
        return memberInfo switch
        {
            PropertyInfo property => property.CanRead && property.GetIndexParameters().Length == 0,
            FieldInfo => true,
            _ => false,
        };
    }

    internal static object? GetValue(this MemberInfo memberInfo, object? instance = null)
    {
        return memberInfo switch
        {
            PropertyInfo property => property.GetValue(instance),
            FieldInfo field => field.GetValue(instance),
            _ => null,
        };
    }

    internal static bool CanSetValue(this MemberInfo memberInfo)
    {
        return memberInfo switch
        {
            PropertyInfo property => property.CanWrite && property.GetIndexParameters().Length == 0,
            FieldInfo => true,
            _ => false,
        };
    }

    internal static void SetValue(this MemberInfo memberInfo, object value, object? instance = null)
    {
        switch (memberInfo)
        {
            case PropertyInfo property:
                property.SetValue(instance, value);
                break;
            case FieldInfo field:
                field.SetValue(instance, value);
                break;
        }
    }

    internal static void CopyFieldsFrom<T>(this T destination, T source, bool disposeOriginalFieldValues = true)
    {
        foreach(var field in typeof(T).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if(disposeOriginalFieldValues && field.GetValue(destination) is IDisposable originalFieldValue)
            {
                originalFieldValue.Dispose();
            }

            field.SetValue(destination, field.GetValue(source));
        }
    }

    internal static void TryDispose(this object obj)
    {
        if(obj is not IDisposable disposable) return;

        disposable.Dispose();
    }
}
