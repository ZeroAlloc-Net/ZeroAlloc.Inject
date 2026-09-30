namespace ZeroAlloc.Inject;

/// <summary>
/// Registers the class as a service with the lifetime of the derived attribute, as its interfaces and
/// its concrete type, or only as <see cref="As"/>.
/// </summary>
/// <remarks>
/// A class with several interfaces resolves one instance per lifetime through every interface and its
/// concrete type, in every container mode. On Microsoft DI each interface forwards to the concrete
/// registration, and Microsoft DI disposes a disposable instance once per registration it resolved it
/// through, so its <see cref="IDisposable.Dispose"/> must be idempotent, as .NET guidance requires.
/// The hybrid and standalone containers dispose it once.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public abstract class ServiceAttribute : Attribute
{
    public Type? As { get; set; }
    public string? Key { get; set; }
    public bool AllowMultiple { get; set; }
}
