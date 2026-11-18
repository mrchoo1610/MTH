namespace DiskCleaner.Core;

/// <summary>Trừu tượng hóa biến môi trường để test có thể giả lập.</summary>
public interface IEnvironment
{
    string? Get(string name);
}

public interface ILogger
{
    void Info(string message);
    void Error(string message, Exception? ex = null);
}

public sealed class NullLogger : ILogger
{
    public static readonly NullLogger Instance = new();
    public void Info(string message) { }
    public void Error(string message, Exception? ex = null) { }
}
