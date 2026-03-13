namespace ZeroInject;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class TransientAttribute : ServiceAttribute;
