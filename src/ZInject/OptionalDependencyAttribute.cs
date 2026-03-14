namespace ZInject;

[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
public sealed class OptionalDependencyAttribute : Attribute { }
