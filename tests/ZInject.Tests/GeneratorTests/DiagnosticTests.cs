using System;
using Microsoft.CodeAnalysis;

namespace ZInject.Tests.GeneratorTests;

public class DiagnosticTests
{
    [Fact]
    public void NoPublicConstructor_ProducesZI006()
    {
        var source = """
            using ZInject;
            namespace TestApp;

            [Transient]
            public class NoCtorService
            {
                private NoCtorService() { }
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Contains(diagnostics, d => string.Equals(d.Id, "ZI006", StringComparison.Ordinal));
    }

    [Fact]
    public void NoInterfaces_ProducesZI007()
    {
        var source = """
            using ZInject;
            namespace TestApp;

            [Transient]
            public class PlainService { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Contains(diagnostics, d => string.Equals(d.Id, "ZI007", StringComparison.Ordinal));
    }

    [Fact]
    public void WithInterfaces_DoesNotProduceZI007()
    {
        var source = """
            using ZInject;
            namespace TestApp;

            public interface IMyService { }

            [Transient]
            public class MyService : IMyService { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => string.Equals(d.Id, "ZI007", StringComparison.Ordinal));
    }

    [Fact]
    public void WithPublicConstructor_DoesNotProduceZI006()
    {
        var source = """
            using ZInject;
            namespace TestApp;

            [Transient]
            public class GoodService
            {
                public GoodService() { }
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => string.Equals(d.Id, "ZI006", StringComparison.Ordinal));
    }

    [Fact]
    public void ImplicitDefaultConstructor_DoesNotProduceZI006()
    {
        var source = """
            using ZInject;
            namespace TestApp;

            public interface IMyService { }

            [Transient]
            public class ImplicitCtorService : IMyService { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => string.Equals(d.Id, "ZI006", StringComparison.Ordinal));
    }

    [Fact]
    public void MixedConstructors_PublicExists_DoesNotProduceZI006()
    {
        var source = """
            using ZInject;
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

        Assert.DoesNotContain(diagnostics, d => string.Equals(d.Id, "ZI006", StringComparison.Ordinal));
    }

    [Fact]
    public void AbstractClass_IsSkipped()
    {
        var source = """
            using ZInject;
            namespace TestApp;

            public interface IMyService { }

            [Transient]
            public abstract class AbstractService : IMyService { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.DoesNotContain("AbstractService", output);
    }

    [Fact]
    public void ZI011_DecoratorWithNoMatchingInterface_ReportsError()
    {
        var source = """
            using ZInject;
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
        Assert.Contains(diagnostics, static d => string.Equals(d.Id, "ZI011", StringComparison.Ordinal));
    }

    [Fact]
    public void ZI012_DecoratorWithNoRegisteredInner_ReportsError()
    {
        var source = """
            using ZInject;
            public interface IFoo { }
            [Decorator]
            public class LoggingFoo : IFoo
            {
                public LoggingFoo(IFoo inner) { }
            }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);
        Assert.Contains(diagnostics, static d => string.Equals(d.Id, "ZI012", StringComparison.Ordinal));
    }

    [Fact]
    public void ZI013_AbstractDecorator_ReportsWarning()
    {
        var source = """
            using ZInject;
            public interface IFoo { }
            [Decorator]
            public abstract class LoggingFoo : IFoo
            {
                public LoggingFoo(IFoo inner) { }
            }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);
        Assert.Contains(diagnostics, static d => string.Equals(d.Id, "ZI013", StringComparison.Ordinal));
    }

    [Fact]
    public void ZI014_CircularDependency_AB_ReportsError()
    {
        var source = """
            using ZInject;
            public interface IA { }
            public interface IB { }
            [Transient]
            public class A : IA { public A(IB b) { } }
            [Transient]
            public class B : IB { public B(IA a) { } }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);
        Assert.Contains(diagnostics, static d => string.Equals(d.Id, "ZI014", StringComparison.Ordinal));
    }

    [Fact]
    public void ZI014_NoCycle_NoDiagnostic()
    {
        var source = """
            using ZInject;
            public interface IA { }
            public interface IB { }
            [Transient]
            public class A : IA { public A(IB b) { } }
            [Transient]
            public class B : IB { }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);
        Assert.DoesNotContain(diagnostics, static d => string.Equals(d.Id, "ZI014", StringComparison.Ordinal));
    }

    [Fact]
    public void ZI014_ThreeNodeCycle_ReportsError()
    {
        var source = """
            using ZInject;
            public interface IA { }
            public interface IB { }
            public interface IC { }
            [Transient] public class A : IA { public A(IB b) { } }
            [Transient] public class B : IB { public B(IC c) { } }
            [Transient] public class C : IC { public C(IA a) { } }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);
        Assert.Contains(diagnostics, static d => string.Equals(d.Id, "ZI014", StringComparison.Ordinal));
    }

    [Fact]
    public void ZI014_OptionalDependencyBreaksCycle_NoDiagnostic()
    {
        var source = """
            using ZInject;
            public interface IA { }
            public interface IB { }
            [Transient]
            public class A : IA { public A(IB b = null) { } }
            [Transient]
            public class B : IB { public B(IA a) { } }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);
        Assert.DoesNotContain(diagnostics, static d => string.Equals(d.Id, "ZI014", StringComparison.Ordinal));
    }

    [Fact]
    public void ZI014_DecoratorSelfReference_NotFlagged()
    {
        var source = """
            using ZInject;
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
        Assert.DoesNotContain(diagnostics, static d => string.Equals(d.Id, "ZI014", StringComparison.Ordinal));
    }

    [Fact]
    public void OptionalDependency_GeneratesGetService_InsteadOfGetRequiredService()
    {
        var source = """
            using ZInject;
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
    public void OptionalDependency_OnNonNullableParameter_ReportsZI015()
    {
        var source = """
            #nullable enable
            using ZInject;
            public interface ILogger { }
            public interface IFoo { }
            [Transient]
            public class FooImpl : IFoo
            {
                public FooImpl([OptionalDependency] ILogger logger) { }
            }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);
        Assert.Contains(diagnostics, static d => string.Equals(d.Id, "ZI015", StringComparison.Ordinal));
    }

    [Fact]
    public void DecoratorOf_InterfaceNotImplemented_ReportsZI016()
    {
        var source = """
            using ZInject;
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
        Assert.Contains(diagnostics, static d => string.Equals(d.Id, "ZI016", StringComparison.Ordinal));
        Assert.DoesNotContain(diagnostics, static d => string.Equals(d.Id, "ZI011", StringComparison.Ordinal));
    }

    [Fact]
    public void DecoratorOf_DuplicateOrder_ReportsZI017()
    {
        var source = """
            using ZInject;
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
        Assert.Contains(diagnostics, static d => string.Equals(d.Id, "ZI017", StringComparison.Ordinal));
    }
}
