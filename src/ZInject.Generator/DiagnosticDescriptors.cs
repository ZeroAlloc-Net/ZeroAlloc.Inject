using Microsoft.CodeAnalysis;

namespace ZInject.Generator
{
    internal static class DiagnosticDescriptors
    {
        public static readonly DiagnosticDescriptor MultipleLifetimeAttributes = new DiagnosticDescriptor(
            "ZI001",
            "Multiple lifetime attributes",
            "Class '{0}' has multiple lifetime attributes; only one of [Transient], [Scoped], or [Singleton] is allowed",
            "ZInject",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor AttributeOnNonClass = new DiagnosticDescriptor(
            "ZI002",
            "Attribute on non-class type",
            "'{0}' is not a class; service attributes can only be applied to classes",
            "ZInject",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor AttributeOnAbstractOrStatic = new DiagnosticDescriptor(
            "ZI003",
            "Attribute on abstract or static class",
            "Class '{0}' is abstract or static and cannot be registered as a service",
            "ZInject",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor AsTypeNotImplemented = new DiagnosticDescriptor(
            "ZI004",
            "As type not implemented",
            "Class '{0}' does not implement '{1}' specified in the As property",
            "ZInject",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor KeyedServiceNotSupported = new DiagnosticDescriptor(
            "ZI005",
            "Keyed services require .NET 8+",
            "Class '{0}' uses Key property but the target framework does not support keyed services (requires .NET 8+)",
            "ZInject",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor NoPublicConstructor = new DiagnosticDescriptor(
            "ZI006",
            "No public constructor",
            "Class '{0}' has no public constructor; the DI container requires a public constructor to resolve this service",
            "ZInject",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor NoInterfaces = new DiagnosticDescriptor(
            "ZI007",
            "No interfaces implemented",
            "Class '{0}' implements no interfaces and will only be registered as its concrete type",
            "ZInject",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor MissingDIAbstractions = new DiagnosticDescriptor(
            "ZI008",
            "Missing DI abstractions",
            "Microsoft.Extensions.DependencyInjection.Abstractions is not referenced and generated code will not compile",
            "ZInject",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor MultipleConstructorsNoAttribute = new DiagnosticDescriptor(
            "ZI009",
            "Multiple public constructors without [ActivatorUtilitiesConstructor]",
            "Class '{0}' has multiple public constructors; apply [ActivatorUtilitiesConstructor] to the preferred constructor",
            "ZInject",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor PrimitiveConstructorParameter = new DiagnosticDescriptor(
            "ZI010",
            "Constructor parameter is a primitive/value type",
            "Constructor parameter '{0}' of class '{1}' is a primitive/value type ({2}); use IOptions<T> or a wrapper type instead",
            "ZInject",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor DecoratorNoMatchingInterface = new DiagnosticDescriptor(
            "ZI011",
            "Decorator has no matching interface",
            "Class '{0}' is marked [Decorator] but no constructor parameter type matches any interface it implements",
            "ZInject",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor DecoratorNoRegisteredInner = new DiagnosticDescriptor(
            "ZI012",
            "Decorator inner service not found",
            "Class '{0}' is marked [Decorator] for '{1}' but no service implementing that interface is registered in this assembly",
            "ZInject",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor DecoratorOnAbstractOrStatic = new DiagnosticDescriptor(
            "ZI013",
            "Decorator on abstract or static class",
            "Class '{0}' is abstract or static and cannot be used as a decorator",
            "ZInject",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor CircularDependency = new DiagnosticDescriptor(
            "ZI014",
            "Circular dependency detected",
            "Circular dependency detected: {0}",
            "ZInject",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor OptionalDependencyOnNonNullable = new DiagnosticDescriptor(
            "ZI015",
            "[OptionalDependency] on non-nullable parameter",
            "Parameter '{0}' of class '{1}' is marked [OptionalDependency] but its type '{2}' is not nullable; change the parameter type to '{2}?'",
            "ZInject",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor DecoratorOfInterfaceNotImplemented = new DiagnosticDescriptor(
            "ZI016",
            "[DecoratorOf] interface not implemented",
            "Class '{0}' is marked [DecoratorOf({1})] but does not implement that interface",
            "ZInject",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor DecoratorOfDuplicateOrder = new DiagnosticDescriptor(
            "ZI017",
            "Duplicate decorator Order",
            "Interface '{0}' has two [DecoratorOf] decorators with the same Order={1}: '{2}' and '{3}'. Orders must be unique per interface.",
            "ZInject",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);
    }
}
