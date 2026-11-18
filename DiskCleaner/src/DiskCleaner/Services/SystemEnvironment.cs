using DiskCleaner.Core;

namespace DiskCleaner.Services;

public sealed class SystemEnvironment : IEnvironment
{
    public string? Get(string name) => Environment.GetEnvironmentVariable(name);
}
