namespace ZeroAlloc.Inject;

[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
public sealed class OptionalDependencyAttribute : Attribute { }
