using System;

namespace ConfigLib;

public class ConfigLibException : Exception
{
    public ConfigLibException() { }
    public ConfigLibException(string message) : base(message) { }
    public ConfigLibException(string message, Exception exception) : base(message, exception) { }
}
public class InvalidTokenException : ConfigLibException
{
    public InvalidTokenException() { }
    public InvalidTokenException(string message) : base(message) { }
    public InvalidTokenException(string message, Exception exception) : base(message, exception) { }
}
public class InvalidConfigException : ConfigLibException
{
    public InvalidConfigException() { }
    public InvalidConfigException(string message) : base(message) { }
    public InvalidConfigException(string message, Exception exception) : base(message, exception) { }
}
