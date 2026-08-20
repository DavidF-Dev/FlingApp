namespace Fling.Config;

public sealed class ConfigException : Exception
{
    public ConfigException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
