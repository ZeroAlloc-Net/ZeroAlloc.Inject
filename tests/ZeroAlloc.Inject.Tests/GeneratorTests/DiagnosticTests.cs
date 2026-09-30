using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Inject.Tests.GeneratorTests;

public class DiagnosticTests
{
    [Fact]
    public void NoPublicConstructor_ProducesZAI006()
    {
        var source = """
            using ZeroAlloc.Inject;
            namespace TestApp;

            [Transient]
            public class NoCtorService
            {
                private NoCtorService() { }
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Contains(diagnostics, d => string.Equals(d.Id, "ZAI006", StringComparison.Ordinal));
    }

    [Fact]
    public void NoInterfaces_ProducesZAI007()
    {
        var source = """
            using ZeroAlloc.Inject;
            namespace TestApp;

            [Transient]
            public class PlainService { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Contains(diagnostics, d => string.Equals(d.Id, "ZAI007", StringComparison.Ordinal));
    }

    [Fact]
    public void WithInterfaces_DoesNotProduceZAI007()
    {
        var source = """
            using ZeroAlloc.Inject;
            namespace TestApp;

            public interface IMyService { }

            [Transient]
            public class MyService : IMyService { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => string.Equals(d.Id, "ZAI007", StringComparison.Ordinal));
    }

    [Fact]
    public void WithPublicConstructor_DoesNotProduceZAI006()
    {
        var source = """
            using ZeroAlloc.Inject;
            namespace TestApp;

            [Transient]
            public class GoodService
            {
                public GoodService() { }
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => string.Equals(d.Id, "ZAI006", StringComparison.Ordinal));
    }

    [Fact]
    public void ImplicitDefaultConstructor_DoesNotProduceZAI006()
    {
        var source = """
            using ZeroAlloc.Inject;
            namespace TestApp;

            public interface IMyService { }

            [Transient]
            public class ImplicitCtorService : IMyService { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => string.Equals(d.Id, "ZAI006", StringComparison.Ordinal));
    }

    [Fact]
    public void MixedConstructors_PublicExists_DoesNotProduceZAI006()
    {
        var source = """
            using ZeroAlloc.Inject;
            namespace TestApp;

            public interface IMyService { }

            [Transient]
            public class MultiCtorService : IMyService
            {
                public MultiCtorService() { }
                private MultiCtorService(string x) { }
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => string.Equals(d.Id, "ZAI006", StringComparison.Ordinal));
    }

    [Fact]
    public void AbstractClass_IsSkipped()
    {
        var source = """
            using ZeroAlloc.Inject;
            namespace TestApp;

            public interface IMyService { }

            [Transient]
            public abstract class AbstractService : IMyService { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.DoesNotContain("AbstractService", output);
    }

    [Fact]
    public void ZAI011_DecoratorWithNoMatchingInterface_ReportsError()
    {
        var source = """
            using ZeroAlloc.Inject;
            public interface IFoo { }
            public interface IBar { }
            [Decorator]
            public class LoggingFoo : IFoo
            {
                public LoggingFoo(IBar unrelated) { }
            }
            [Transient]
            public class FooImpl : IFoo { }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);
        Assert.Contains(diagnostics, static d => string.Equals(d.Id, "ZAI011", StringComparison.Ordinal));
    }

    [Fact]
    public void ZAI012_DecoratorWithNoRegisteredInner_ReportsError()
    {
        var source = """
            using ZeroAlloc.Inject;
            public interface IFoo { }
            [Decorator]
            public class LoggingFoo : IFoo
            {
                public LoggingFoo(IFoo inner) { }
            }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);
        Assert.Contains(diagnostics, static d => string.Equals(d.Id, "ZAI012", StringComparison.Ordinal));
    }

    [Fact]
    public void ZAI013_AbstractDecorator_ReportsWarning()
    {
        var source = """
            using ZeroAlloc.Inject;
            public interface IFoo { }
            [Decorator]
            public abstract class LoggingFoo : IFoo
            {
                public LoggingFoo(IFoo inner) { }
            }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);
        Assert.Contains(diagnostics, static d => string.Equals(d.Id, "ZAI013", StringComparison.Ordinal));
    }

    [Fact]
    public void ZAI014_CircularDependency_AB_ReportsError()
    {
        var source = """
            using ZeroAlloc.Inject;
            public interface IA { }
            public interface IB { }
            [Transient]
            public class A : IA { public A(IB b) { } }
            [Transient]
            public class B : IB { public B(IA a) { } }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);
        Assert.Contains(diagnostics, static d => string.Equals(d.Id, "ZAI014", StringComparison.Ordinal));
    }

    [Fact]
    public void ZAI014_NoCycle_NoDiagnostic()
    {
        var source = """
            using ZeroAlloc.Inject;
            public interface IA { }
            public interface IB { }
            [Transient]
            public class A : IA { public A(IB b) { } }
            [Transient]
            public class B : IB { }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);
        Assert.DoesNotContain(diagnostics, static d => string.Equals(d.Id, "ZAI014", StringComparison.Ordinal));
    }

    [Fact]
    public void ZAI014_ThreeNodeCycle_ReportsError()
    {
        var source = """
            using ZeroAlloc.Inject;
            public interface IA { }
            public interface IB { }
            public interface IC { }
            [Transient] public class A : IA { public A(IB b) { } }
            [Transient] public class B : IB { public B(IC c) { } }
            [Transient] public class C : IC { public C(IA a) { } }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);
        Assert.Contains(diagnostics, static d => string.Equals(d.Id, "ZAI014", StringComparison.Ordinal));
    }

    [Fact]
    public void ZAI014_OptionalDependencyBreaksCycle_NoDiagnostic()
    {
        var source = """
            using ZeroAlloc.Inject;
            public interface IA { }
            public interface IB { }
            [Transient]
            public class A : IA { public A(IB b = null) { } }
            [Transient]
            public class B : IB { public B(IA a) { } }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);
        Assert.DoesNotContain(diagnostics, static d => string.Equals(d.Id, "ZAI014", StringComparison.Ordinal));
    }

    [Fact]
    public void ZAI014_DecoratorSelfReference_NotFlagged()
    {
        var source = """
            using ZeroAlloc.Inject;
            public interface IFoo { }
            [Transient]
            public class FooImpl : IFoo { }
            [Decorator]
            public class LoggingFoo : IFoo
            {
                public LoggingFoo(IFoo inner) { }
            }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);
        Assert.DoesNotContain(diagnostics, static d => string.Equals(d.Id, "ZAI014", StringComparison.Ordinal));
    }

    [Fact]
    public void OptionalDependency_GeneratesGetService_InsteadOfGetRequiredService()
    {
        var source = """
            using ZeroAlloc.Inject;
            public interface IFoo { }
            public interface ILogger { }
            [Transient]
            public class FooImpl : IFoo
            {
                public FooImpl([OptionalDependency] ILogger? logger) { }
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);
        Assert.DoesNotContain(diagnostics, static d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("GetService<global::ILogger>", output);
        Assert.DoesNotContain("GetRequiredService<global::ILogger>", output);
    }

    [Fact]
    public void OptionalDependency_OnNonNullableParameter_ReportsZAI015()
    {
        var source = """
            #nullable enable
            using ZeroAlloc.Inject;
            public interface ILogger { }
            public interface IFoo { }
            [Transient]
            public class FooImpl : IFoo
            {
                public FooImpl([OptionalDependency] ILogger logger) { }
            }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);
        Assert.Contains(diagnostics, static d => string.Equals(d.Id, "ZAI015", StringComparison.Ordinal));
    }

    [Fact]
    public void DecoratorOf_InterfaceNotImplemented_ReportsZAI016()
    {
        var source = """
            using ZeroAlloc.Inject;
            public interface IFoo { }
            public interface IBar { }
            [Transient]
            public class FooImpl : IFoo { }
            [DecoratorOf(typeof(IBar))]
            public class BadDecorator : IFoo
            {
                public BadDecorator(IFoo inner) { }
            }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);
        Assert.Contains(diagnostics, static d => string.Equals(d.Id, "ZAI016", StringComparison.Ordinal));
        Assert.DoesNotContain(diagnostics, static d => string.Equals(d.Id, "ZAI011", StringComparison.Ordinal));
    }

    [Fact]
    public void DecoratorOf_DuplicateOrder_ReportsZAI017()
    {
        var source = """
            using ZeroAlloc.Inject;
            public interface IFoo { }
            [Transient]
            public class FooImpl : IFoo { }
            [DecoratorOf(typeof(IFoo), Order = 1)]
            public class DecoratorA : IFoo
            {
                public DecoratorA(IFoo inner) { }
            }
            [DecoratorOf(typeof(IFoo), Order = 1)]
            public class DecoratorB : IFoo
            {
                public DecoratorB(IFoo inner) { }
            }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);
        Assert.Contains(diagnostics, static d => string.Equals(d.Id, "ZAI017", StringComparison.Ordinal));
    }

    [Fact]
    public void ZAI018_OpenGenericNoDetectedUsages_ReportsWarning()
    {
        var source = """
            using ZeroAlloc.Inject;
            public interface IRepository<T> { }
            [Transient]
            public class Repository<T> : IRepository<T> { }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);
        Assert.Contains(diagnostics, static d => string.Equals(d.Id, "ZAI018", StringComparison.Ordinal));
        Assert.All(
            diagnostics.AsEnumerable().Where(static d => string.Equals(d.Id, "ZAI018", StringComparison.Ordinal)).ToList(),
            static d => Assert.Equal(DiagnosticSeverity.Warning, d.Severity));
    }

    [Fact]
    public void ZAI018_OpenGenericWithDetectedUsage_NoWarning()
    {
        var source = """
            using ZeroAlloc.Inject;
            public interface IRepository<T> { }
            [Transient]
            public class Repository<T> : IRepository<T> { }
            [Transient]
            public class OrderService
            {
                public OrderService(IRepository<string> repo) { }
            }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);
        Assert.DoesNotContain(diagnostics, static d => string.Equals(d.Id, "ZAI018", StringComparison.Ordinal));
    }

    [Fact]
    public void ZAI018_OpenGenericWithAsNarrowing_NoDetectedUsages_ReportsWarning()
    {
        // As narrows to IReadRepo<> but no consumer of IReadRepo<SomeType> exists
        var source = """
            using ZeroAlloc.Inject;
            public interface IReadRepo<T> { }
            public interface IWriteRepo<T> { }
            [Transient(As = typeof(IReadRepo<>))]
            public class Repo<T> : IReadRepo<T>, IWriteRepo<T> { }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);
        Assert.Contains(diagnostics, static d => string.Equals(d.Id, "ZAI018", StringComparison.Ordinal));
    }

    [Fact]
    public void ZAI018_OpenGenericWithAsNarrowing_WithDetectedUsage_NoWarning()
    {
        var source = """
            using ZeroAlloc.Inject;
            public interface IReadRepo<T> { }
            public interface IWriteRepo<T> { }
            [Transient(As = typeof(IReadRepo<>))]
            public class Repo<T> : IReadRepo<T>, IWriteRepo<T> { }
            [Transient]
            public class OrderService
            {
                public OrderService(IReadRepo<string> repo) { }
            }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);
        Assert.DoesNotContain(diagnostics, static d => string.Equals(d.Id, "ZAI018", StringComparison.Ordinal));
    }

    [Fact]
    public void ZAI018_OpenGenericWithConcreteClosedUsage_NoWarning()
    {
        // The concrete type is registered too, so Repository<string> is a closed usage of it.
        var source = """
            using ZeroAlloc.Inject;
            public interface IRepository<T> { }
            [Transient]
            public class Repository<T> : IRepository<T> { }
            [Transient]
            public class OrderService
            {
                public OrderService(Repository<string> repo) { }
            }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);
        Assert.DoesNotContain(diagnostics, static d => string.Equals(d.Id, "ZAI018", StringComparison.Ordinal));
    }

    [Fact]
    public void ZAI018_OpenGenericWithAsNarrowing_ConcreteClosedUsage_ReportsWarning()
    {
        // With As, only IReadRepo<> is registered: Repo<string> is not a service to close.
        var source = """
            using ZeroAlloc.Inject;
            public interface IReadRepo<T> { }
            [Transient(As = typeof(IReadRepo<>))]
            public class Repo<T> : IReadRepo<T> { }
            [Transient]
            public class OrderService
            {
                public OrderService(Repo<string> repo) { }
            }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);
        Assert.Contains(diagnostics, static d => string.Equals(d.Id, "ZAI018", StringComparison.Ordinal));
    }

    // Asserts that exactly one diagnostic with the given ID was reported and returns it.
    private static Diagnostic ExactlyOne(ImmutableArray<Diagnostic> diagnostics, string id)
    {
        Assert.Equal(1, diagnostics.AsEnumerable().Count(d => string.Equals(d.Id, id, StringComparison.Ordinal)));
        return diagnostics.First(d => string.Equals(d.Id, id, StringComparison.Ordinal));
    }

    // ZAI001: multiple lifetime attributes

    [Fact]
    public void ZAI001_TwoLifetimeAttributes_ReportsErrorOnceAndSkipsRegistration()
    {
        var source = """
            using ZeroAlloc.Inject;
            namespace TestApp;
            public interface IOrderService { }
            [Transient]
            [Singleton]
            public class OrderService : IOrderService { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        var zai001 = ExactlyOne(diagnostics, "ZAI001");
        Assert.Equal(DiagnosticSeverity.Error, zai001.Severity);
        Assert.Contains("OrderService", zai001.GetMessage(), StringComparison.Ordinal);
        Assert.DoesNotContain("global::TestApp.OrderService", output, StringComparison.Ordinal);
    }

    [Fact]
    public void ZAI001_ThreeLifetimeAttributes_ReportsErrorOnce()
    {
        var source = """
            using ZeroAlloc.Inject;
            namespace TestApp;
            public interface IOrderService { }
            [Transient, Scoped, Singleton]
            public class OrderService : IOrderService { }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        ExactlyOne(diagnostics, "ZAI001");
    }

    [Fact]
    public void ZAI001_SingleLifetimeAttributePerClass_NoDiagnostic()
    {
        var source = """
            using ZeroAlloc.Inject;
            namespace TestApp;
            public interface IOrderService { }
            [Transient]
            public class OrderService : IOrderService { }
            [Singleton]
            public class OtherOrderService : IOrderService { }
            [Scoped]
            public partial class PartialService : IOrderService { }
            public partial class PartialService { }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, static d => string.Equals(d.Id, "ZAI001", StringComparison.Ordinal));
    }

    // ZAI003: lifetime attribute on an abstract or static class

    [Fact]
    public void ZAI003_AbstractClass_ReportsError()
    {
        var source = """
            using ZeroAlloc.Inject;
            namespace TestApp;
            public interface INotificationService { }
            [Singleton]
            public abstract class NotificationBase : INotificationService { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        var zai003 = ExactlyOne(diagnostics, "ZAI003");
        Assert.Equal(DiagnosticSeverity.Error, zai003.Severity);
        Assert.Contains("NotificationBase", zai003.GetMessage(), StringComparison.Ordinal);
        Assert.DoesNotContain("NotificationBase", output, StringComparison.Ordinal);
    }

    [Fact]
    public void ZAI003_StaticClass_ReportsErrorAndNoOtherServiceDiagnostics()
    {
        var source = """
            using ZeroAlloc.Inject;
            namespace TestApp;
            [Transient]
            public static class Helpers { }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        ExactlyOne(diagnostics, "ZAI003");
        // A static class has no constructor and no interfaces; ZAI003 is the only thing to fix.
        Assert.DoesNotContain(diagnostics, static d => string.Equals(d.Id, "ZAI006", StringComparison.Ordinal));
        Assert.DoesNotContain(diagnostics, static d => string.Equals(d.Id, "ZAI007", StringComparison.Ordinal));
    }

    [Fact]
    public void ZAI003_ConcreteClassDerivedFromAbstractBase_NoDiagnostic()
    {
        var source = """
            using ZeroAlloc.Inject;
            namespace TestApp;
            public interface INotificationService { }
            public abstract class NotificationBase : INotificationService { }
            [Singleton]
            public sealed class EmailNotificationService : NotificationBase { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, static d => string.Equals(d.Id, "ZAI003", StringComparison.Ordinal));
        Assert.Contains("EmailNotificationService", output, StringComparison.Ordinal);
    }

    // ZAI004: As type not implemented

    [Fact]
    public void ZAI004_AsInterfaceNotImplemented_ReportsError()
    {
        var source = """
            using ZeroAlloc.Inject;
            namespace TestApp;
            public interface IPaymentGateway { }
            public interface IEmailSender { }
            [Transient(As = typeof(IPaymentGateway))]
            public class StripeClient : IEmailSender { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        var zai004 = ExactlyOne(diagnostics, "ZAI004");
        Assert.Equal(DiagnosticSeverity.Error, zai004.Severity);
        Assert.Contains("StripeClient", zai004.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("IPaymentGateway", zai004.GetMessage(), StringComparison.Ordinal);
        Assert.DoesNotContain("StripeClient", output, StringComparison.Ordinal);
    }

    [Fact]
    public void ZAI004_OpenGenericAsNotImplemented_ReportsError()
    {
        var source = """
            using ZeroAlloc.Inject;
            namespace TestApp;
            public interface IReadRepo<T> { }
            public interface IWriteRepo<T> { }
            [Scoped(As = typeof(IReadRepo<>))]
            public class Repo<T> : IWriteRepo<T> { }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        ExactlyOne(diagnostics, "ZAI004");
    }

    [Fact]
    public void ZAI004_AsTypeImplementedOrInherited_NoDiagnostic()
    {
        var source = """
            using System;
            using ZeroAlloc.Inject;
            namespace TestApp;
            public interface IFoo { }
            public interface IBar : IFoo { }
            public interface IReadRepo<T> { }
            public interface IHandler<T> { }
            public abstract class ServiceBase { }

            [Transient(As = typeof(IFoo))]
            public class Direct : IFoo, IDisposable { public void Dispose() { } }

            [Transient(As = typeof(IFoo))]
            public class ViaDerivedInterface : IBar { }

            [Transient(As = typeof(IFoo))]
            public class ViaBaseClass : Direct { }

            [Transient(As = typeof(ServiceBase))]
            public class DerivedFromBase : ServiceBase { }

            [Transient(As = typeof(SelfRegistered))]
            public class SelfRegistered { }

            [Transient(As = typeof(IDisposable))]
            public class FilteredSystemInterface : IDisposable { public void Dispose() { } }

            [Scoped(As = typeof(IReadRepo<>))]
            public class Repo<T> : IReadRepo<T> { }

            [Transient(As = typeof(IHandler<string>))]
            public class StringHandler : IHandler<string> { }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, static d => string.Equals(d.Id, "ZAI004", StringComparison.Ordinal));
    }

    // ZAI008: Microsoft.Extensions.DependencyInjection.Abstractions not referenced

    [Fact]
    public void ZAI008_NoDependencyInjectionAbstractions_ReportsWarningOnce()
    {
        var source = """
            using ZeroAlloc.Inject;
            namespace TestApp;
            public interface IMyService { }
            public interface IOtherService { }
            [Transient]
            public class MyService : IMyService { }
            [Singleton]
            public class OtherService : IOtherService { }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGeneratorWithoutDependencyInjection(source);

        var zai008 = ExactlyOne(diagnostics, "ZAI008");
        Assert.Equal(DiagnosticSeverity.Warning, zai008.Severity);
    }

    [Fact]
    public void ZAI008_NoServices_NoDiagnostic()
    {
        var source = """
            namespace TestApp;
            public class NotAService { }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGeneratorWithoutDependencyInjection(source);

        Assert.DoesNotContain(diagnostics, static d => string.Equals(d.Id, "ZAI008", StringComparison.Ordinal));
    }

    [Fact]
    public void ZAI008_DependencyInjectionAbstractionsReferenced_NoDiagnostic()
    {
        var source = """
            using ZeroAlloc.Inject;
            namespace TestApp;
            public interface IMyService { }
            [Transient]
            public class MyService : IMyService { }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, static d => string.Equals(d.Id, "ZAI008", StringComparison.Ordinal));
    }

    // --- ZAI021: a service type registered more than once without AllowMultiple ---

    private static List<Diagnostic> Zai021(string source)
    {
        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);
        return diagnostics.AsEnumerable().Where(static d => string.Equals(d.Id, "ZAI021", StringComparison.Ordinal)).ToList();
    }

    [Fact]
    public void ZAI021_TwoServicesOfOneInterface_WithoutAllowMultiple_WarnsAtTheLaterOne()
    {
        var found = Zai021("""
            using ZeroAlloc.Inject;
            public interface IFoo { }
            [Transient]
            public class First : IFoo { }
            [Transient]
            public class Second : IFoo { }
            """);

        var diagnostic = AssertEx.One(found);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("'Second'", diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.Contains("'IFoo'", diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.Contains("'First'", diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public void ZAI021_AcrossLifetimes_WarnsOncePerLaterRegistration()
    {
        // The extension registers transients, then scoped services, then singletons.
        var found = Zai021("""
            using ZeroAlloc.Inject;
            public interface IFoo { }
            [Singleton]
            public class C : IFoo { }
            [Scoped]
            public class B : IFoo { }
            [Transient]
            public class A : IFoo { }
            """);

        Assert.Equal(2, found.Count);
        Assert.All(found, static d => Assert.Contains("'A'", d.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("AllowMultiple = true", "AllowMultiple = true", false)]
    [InlineData("", "AllowMultiple = true", false)]
    [InlineData("AllowMultiple = true", "", true)]
    public void ZAI021_ReportsALaterRegistrationWithoutAllowMultiple(string first, string second, bool expected)
    {
        var found = Zai021($$"""
            using ZeroAlloc.Inject;
            public interface IFoo { }
            [Transient({{first}})]
            public class First : IFoo { }
            [Transient({{second}})]
            public class Second : IFoo { }
            """);

        Assert.Equal(expected ? 1 : 0, found.Count);
    }

    [Fact]
    public void ZAI021_KeyedRegistrations_ConflictOnlyUnderTheSameKey()
    {
        var found = Zai021("""
            using ZeroAlloc.Inject;
            public interface IFoo { }
            [Transient(Key = "a")]
            public class A1 : IFoo { }
            [Transient(Key = "a")]
            public class A2 : IFoo { }
            [Transient(Key = "b")]
            public class B : IFoo { }
            [Transient]
            public class Unkeyed : IFoo { }
            """);

        Assert.Contains("'A2'", AssertEx.One(found).GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public void ZAI021_OpenGenerics_OfOneInterface_Warn()
    {
        var found = Zai021("""
            using ZeroAlloc.Inject;
            public interface IRepo<T> { }
            [Transient]
            public class First<T> : IRepo<T> { }
            [Transient]
            public class Second<T> : IRepo<T> { }
            [Transient]
            public class Consumer { public Consumer(IRepo<int> repo) { } }
            """);

        Assert.Contains("'Second'", AssertEx.One(found).GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public void ZAI021_ADecoratedInterface_IsNotReported()
    {
        // The extension registers a decorated interface with Add, so every mode resolves the last.
        var found = Zai021("""
            using ZeroAlloc.Inject;
            public interface IFoo { }
            [Transient]
            public class First : IFoo { }
            [Transient]
            public class Second : IFoo { }
            [Decorator]
            public class LoggingFoo : IFoo { public LoggingFoo(IFoo inner) { } }
            """);

        Assert.Empty(found);
    }

    [Fact]
    public void ZAI021_DistinctServiceTypes_AreNotReported()
    {
        var found = Zai021("""
            using ZeroAlloc.Inject;
            public interface IFoo { }
            public interface IBar { }
            [Transient]
            public class Foo : IFoo { }
            [Transient]
            public class Bar : IBar { }
            [Transient(As = typeof(IFoo))]
            public class Narrow : IFoo, IBar { }
            """);

        // Narrow registers only IFoo, which Foo already does.
        Assert.Contains("'Narrow'", AssertEx.One(found).GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }
}
