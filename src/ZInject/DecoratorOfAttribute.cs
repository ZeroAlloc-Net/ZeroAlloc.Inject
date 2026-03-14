namespace ZInject;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class DecoratorOfAttribute : Attribute
{
    public DecoratorOfAttribute(Type decoratedInterface)
    {
        DecoratedInterface = decoratedInterface;
    }

    public Type DecoratedInterface { get; }
    public int Order { get; set; }
    public Type? WhenRegistered { get; set; }
}
