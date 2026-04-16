using System;
using System.Collections.Generic;
using System.Text;

namespace configlib.source.Typed.Extensions;

public static class TypeExtensions
{
    extension(Type)
    {
        public static Type? GetTypeSafe(string name)
        {
            if(string.IsNullOrWhiteSpace(name)) return null;
            try
            {
                return Type.GetType(name, throwOnError: false);
            }
            catch
            {
                return null;
            }
        }
    }
}
