namespace ZeroInject;

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class ZeroInjectAttribute : Attribute
{
    public string MethodName { get; }

    public ZeroInjectAttribute(string methodName)
    {
        MethodName = methodName;
    }
}
