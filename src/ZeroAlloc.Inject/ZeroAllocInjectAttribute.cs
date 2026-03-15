namespace ZeroAlloc.Inject;

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class ZeroAllocInjectAttribute : Attribute
{
    public string MethodName { get; }

    public ZeroAllocInjectAttribute(string methodName)
    {
        MethodName = methodName;
    }
}
