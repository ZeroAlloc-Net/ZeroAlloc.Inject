using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Inject.Generator
{
    internal static class DiagnosticDescriptors
    {
        public static readonly DiagnosticDescriptor MultipleLifetimeAttributes = new DiagnosticDescriptor(
            "ZAI001",
            "Multiple lifetime attributes",
            "Class '{0}' has multiple lifetime attributes; only one of [Transient], [Scoped], or [Singleton] is allowed",
            "ZeroAlloc.Inject",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor AttributeOnNonClass = new DiagnosticDescriptor(
            "ZAI002",
            "Attribute on non-class type",
            "'{0}' is not a class; service attributes can only be applied to classes",
            "ZeroAlloc.Inject",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor AttributeOnAbstractOrStatic = new DiagnosticDescriptor(
            "ZAI003",
            "Attribute on abstract or static class",
            "Class '{0}' is abstract or static and cannot be registered as a service",
            "ZeroAlloc.Inject",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor AsTypeNotImplemented = new DiagnosticDescriptor(
            "ZAI004",
            "As type not implemented",
            "Class '{0}' does not implement '{1}' specified in the As property",
            "ZeroAlloc.Inject",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor KeyedServiceNotSupported = new DiagnosticDescriptor(
            "ZAI005",
            "Keyed services require .NET 8+",
            "Class '{0}' uses Key property but the target framework does not support keyed services (requires .NET 8+)",
            "ZeroAlloc.Inject",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor NoPublicConstructor = new DiagnosticDescriptor(
            "ZAI006",
            "No public constructor",
            "Class '{0}' has no public constructor; the DI container requires a public constructor to resolve this service",
            "ZeroAlloc.Inject",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor NoInterfaces = new DiagnosticDescriptor(
            "ZAI007",
            "No interfaces implemented",
            "Class '{0}' implements no interfaces and will only be registered as its concrete type",
            "ZeroAlloc.Inject",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor MissingDIAbstractions = new DiagnosticDescriptor(
            "ZAI008",
            "Missing DI abstractions",
            "Microsoft.Extensions.DependencyInjection.Abstractions is not referenced and generated code will not compile",
            "ZeroAlloc.Inject",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor MultipleConstructorsNoAttribute = new DiagnosticDescriptor(
            "ZAI009",
            "Multiple public constructors without [ActivatorUtilitiesConstructor]",
            "Class '{0}' has multiple public constructors; apply [ActivatorUtilitiesConstructor] to the preferred constructor",
            "ZeroAlloc.Inject",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor PrimitiveConstructorParameter = new DiagnosticDescriptor(
            "ZAI010",
            "Constructor parameter is a primitive/value type",
            "Constructor parameter '{0}' of class '{1}' is a primitive/value type ({2}); use IOptions<T> or a wrapper type instead",
            "ZeroAlloc.Inject",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor DecoratorNoMatchingInterface = new DiagnosticDescriptor(
            "ZAI011",
            "Decorator has no matching interface",
            "Class '{0}' is marked [Decorator] but no constructor parameter type matches any interface it implements",
            "ZeroAlloc.Inject",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor DecoratorNoRegisteredInner = new DiagnosticDescriptor(
            "ZAI012",
            "Decorator inner service not found",
            "Class '{0}' is marked [Decorator] for '{1}' but no service implementing that interface is registered in this assembly",
            "ZeroAlloc.Inject",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor DecoratorOnAbstractOrStatic = new DiagnosticDescriptor(
            "ZAI013",
            "Decorator on abstract or static class",
            "Class '{0}' is abstract or static and cannot be used as a decorator",
            "ZeroAlloc.Inject",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor CircularDependency = new DiagnosticDescriptor(
            "ZAI014",
            "Circular dependency detected",
            "Circular dependency detected: {0}",
            "ZeroAlloc.Inject",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor OptionalDependencyOnNonNullable = new DiagnosticDescriptor(
            "ZAI015",
            "[OptionalDependency] on non-nullable parameter",
            "Parameter '{0}' of class '{1}' is marked [OptionalDependency] but its type '{2}' is not nullable; change the parameter type to '{2}?'",
            "ZeroAlloc.Inject",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor DecoratorOfInterfaceNotImplemented = new DiagnosticDescriptor(
            "ZAI016",
            "[DecoratorOf] interface not implemented",
            "Class '{0}' is marked [DecoratorOf({1})] but does not implement that interface",
            "ZeroAlloc.Inject",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor DecoratorOfDuplicateOrder = new DiagnosticDescriptor(
            "ZAI017",
            "Duplicate decorator Order",
            "Interface '{0}' has two [DecoratorOf] decorators with the same Order={1}: '{2}' and '{3}'. Orders must be unique per interface.",
            "ZeroAlloc.Inject",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor NoDetectedClosedUsages = new DiagnosticDescriptor(
            "ZAI018",
            "No closed usages detected for open generic",
            "Open generic '{0}' is registered but no closed usages were detected in this assembly. " +
            "It will not be resolvable from the standalone or hybrid container.",
            "ZeroAlloc.Inject",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);
    }
}
