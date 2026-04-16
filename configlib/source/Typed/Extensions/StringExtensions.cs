namespace configlib.source.Typed.Extensions;

public static class StringExtensions
{
    public static string EnsureEndsWith(this string str, string value)
    {
        if (str.EndsWith(value))
        {
            return str;
        }

        return str + value;
    }
}
