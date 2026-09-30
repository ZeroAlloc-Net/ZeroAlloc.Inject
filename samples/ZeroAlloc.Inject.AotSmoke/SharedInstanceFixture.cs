using ZeroAlloc.Inject;

namespace ZeroAlloc.Inject.AotSmoke;

// A singleton with two interfaces: every mode resolves one instance through both and its concrete type.
public interface ISettingsReader
{
}

public interface ISettingsWriter
{
}

[Singleton]
public sealed class SettingsStore : ISettingsReader, ISettingsWriter
{
}
