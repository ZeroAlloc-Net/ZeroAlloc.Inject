namespace ZeroAlloc.Inject;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public abstract class ServiceAttribute : Attribute
{
    public Type? As { get; set; }
    public string? Key { get; set; }
    public bool AllowMultiple { get; set; }
}
