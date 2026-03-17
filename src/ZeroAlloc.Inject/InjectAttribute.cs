namespace ZeroAlloc.Inject;

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class InjectAttribute : Attribute
{
    public bool Required { get; set; } = true;
}
