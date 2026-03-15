using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Inject.Tests.GeneratorTests;

public class DecoratorGeneratorTests
{
    [Fact]
    public void Decorator_NonGeneric_RegistrationExtension_WrapsInner()
    {
        var source = """
            using ZeroAlloc.Inject;
            public interface IOrderService { }
            [Scoped]
            public class OrderService : IOrderService { }
            [Decorator]
            public class LoggingOrderService : IOrderService
            {
                public LoggingOrderService(IOrderService inner) { }
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);
        Assert.DoesNotContain(diagnostics, static d => d.Severity == DiagnosticSeverity.Error);
        // Inner registered as concrete only (without explicit type arg, inferred by compiler)
        Assert.Contains("TryAddScoped(sp => new global::OrderService", output);
        // Interface registered via factory wrapping inner
        Assert.Contains("AddScoped<global::IOrderService>", output);
        Assert.Contains("GetRequiredService<global::OrderService>", output);
        Assert.Contains("new global::LoggingOrderService", output);
        // Interface NOT registered the old way (no direct TryAdd for the interface)
        Assert.DoesNotContain("TryAddScoped<global::IOrderService>", output);
    }

    [Fact]
    public void Decorator_WithExtraDeps_RegistrationExtension_InjectsAll()
    {
        var source = """
            using ZeroAlloc.Inject;
            public interface IFoo { }
            public interface ILogger { }
            [Transient]
            public class FooImpl : IFoo { }
            [Singleton]
            public class Logger : ILogger { }
            [Decorator]
            public class LoggingFoo : IFoo
            {
                public LoggingFoo(IFoo inner, ILogger logger) { }
            }
            """;

        var (output, _) = GeneratorTestHelper.RunGenerator(source);
        // Inner param: GetRequiredService<FooImpl>()
        Assert.Contains("GetRequiredService<global::FooImpl>", output);
        // Extra dep: GetRequiredService<ILogger>()
        Assert.Contains("GetRequiredService<global::ILogger>", output);
    }

    [Fact]
    public void NoDecorator_Registration_IsUnchanged()
    {
        var source = """
            using ZeroAlloc.Inject;
            public interface IFoo { }
            [Transient]
            public class FooImpl : IFoo { }
            """;

        var (output, _) = GeneratorTestHelper.RunGenerator(source);
        Assert.Contains("TryAddTransient<global::IFoo>", output);
    }

    [Fact]
    public void MultiDecorator_TwoDecorators_ChainsInRegistrationOrder()
    {
        var source = """
            using ZeroAlloc.Inject;
            public interface IRepo { }
            [Transient]
            public class ConcreteRepo : IRepo { }
            [Decorator]
            public class CachingRepo : IRepo
            {
                public CachingRepo(IRepo inner) { }
            }
            [Decorator]
            public class LoggingRepo : IRepo
            {
                public LoggingRepo(IRepo inner) { }
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);
        Assert.DoesNotContain(diagnostics, static d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("new global::LoggingRepo(", output);
        Assert.Contains("new global::CachingRepo(", output);
        Assert.Contains("new global::ConcreteRepo()", output);
    }

    [Fact]
    public void MultiDecorator_ThreeDecorators_ChainsAll()
    {
        var source = """
            using ZeroAlloc.Inject;
            public interface IRepo { }
            [Transient]
            public class ConcreteRepo : IRepo { }
            [Decorator]
            public class CachingRepo : IRepo { public CachingRepo(IRepo inner) { } }
            [Decorator]
            public class LoggingRepo : IRepo { public LoggingRepo(IRepo inner) { } }
            [Decorator]
            public class RetryRepo : IRepo { public RetryRepo(IRepo inner) { } }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);
        Assert.DoesNotContain(diagnostics, static d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("new global::RetryRepo(", output);
        Assert.Contains("new global::LoggingRepo(", output);
        Assert.Contains("new global::CachingRepo(", output);
    }

    [Fact]
    public void MultiDecorator_WithExtraDeps_InjectsAllDeps()
    {
        var source = """
            using ZeroAlloc.Inject;
            public interface IRepo { }
            public interface ILogger { }
            public interface ICache { }
            [Transient] public class RepoImpl : IRepo { }
            [Singleton] public class Logger : ILogger { }
            [Singleton] public class Cache : ICache { }
            [Decorator]
            public class CachingRepo : IRepo { public CachingRepo(IRepo inner, ICache cache) { } }
            [Decorator]
            public class LoggingRepo : IRepo { public LoggingRepo(IRepo inner, ILogger logger) { } }
            """;

        var (output, _) = GeneratorTestHelper.RunGenerator(source);
        Assert.Contains("GetRequiredService<global::ICache>", output);
        Assert.Contains("GetRequiredService<global::ILogger>", output);
    }

    [Fact]
    public void DecoratorOf_BasicWrapping_GeneratesCorrectly()
    {
        var source = """
            using ZeroAlloc.Inject;
            public interface IFoo { }
            [Transient]
            public class FooImpl : IFoo { }
            [DecoratorOf(typeof(IFoo))]
            public class LoggingFoo : IFoo
            {
                public LoggingFoo(IFoo inner) { }
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);
        Assert.DoesNotContain(diagnostics, static d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("new global::LoggingFoo", output);
        Assert.Contains("GetRequiredService<global::FooImpl>", output);
    }

    [Fact]
    public void DecoratorOf_WhenRegistered_EmitsConditionalCheck()
    {
        var source = """
            using ZeroAlloc.Inject;
            public interface IFoo { }
            public class SomeOptions { }
            [Transient]
            public class FooImpl : IFoo { }
            [DecoratorOf(typeof(IFoo), WhenRegistered = typeof(SomeOptions))]
            public class ConditionalFoo : IFoo
            {
                public ConditionalFoo(IFoo inner) { }
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);
        Assert.DoesNotContain(diagnostics, static d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("using System.Linq;", output);
        Assert.Contains("services.Any(d => d.ServiceType == typeof(global::SomeOptions))", output);
        Assert.Contains("new global::ConditionalFoo", output);
    }

    [Fact]
    public void DecoratorOf_NoWhenRegistered_DoesNotEmitConditionalCheck()
    {
        var source = """
            using ZeroAlloc.Inject;
            public interface IFoo { }
            [Transient]
            public class FooImpl : IFoo { }
            [DecoratorOf(typeof(IFoo))]
            public class UnconditionalFoo : IFoo
            {
                public UnconditionalFoo(IFoo inner) { }
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);
        Assert.DoesNotContain(diagnostics, static d => d.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain("services.Any", output);
        Assert.Contains("new global::UnconditionalFoo", output);
    }

    [Fact]
    public void DecoratorOf_Order_InnerMostFirst()
    {
        var source = """
            using ZeroAlloc.Inject;
            public interface IFoo { }
            [Transient]
            public class FooImpl : IFoo { }
            [DecoratorOf(typeof(IFoo), Order = 2)]
            public class OuterFoo : IFoo
            {
                public OuterFoo(IFoo inner) { }
            }
            [DecoratorOf(typeof(IFoo), Order = 1)]
            public class InnerFoo : IFoo
            {
                public InnerFoo(IFoo inner) { }
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);
        Assert.DoesNotContain(diagnostics, static d => d.Severity == DiagnosticSeverity.Error);
        var innerIdx = output.IndexOf("new global::InnerFoo");
        var outerIdx = output.IndexOf("new global::OuterFoo");
        Assert.True(innerIdx >= 0 && outerIdx >= 0, "Both decorators must appear in output");
        Assert.True(outerIdx < innerIdx, "OuterFoo (Order=2) is the outermost wrapper and must appear first in the generated expression");
    }

    [Fact]
    public void DecoratorOf_FullChain_OrderAndWhenRegisteredAndOptional()
    {
        var source = """
            using ZeroAlloc.Inject;
            public interface IRetriever { }
            public class SomeOptions { }
            public interface ILogger { }
            [Transient]
            public class RealRetriever : IRetriever { }
            [DecoratorOf(typeof(IRetriever), Order = 1, WhenRegistered = typeof(SomeOptions))]
            public class LoggingRetriever : IRetriever
            {
                public LoggingRetriever(IRetriever inner, [OptionalDependency] ILogger? logger) { }
            }
            [DecoratorOf(typeof(IRetriever), Order = 2)]
            public class TracingRetriever : IRetriever
            {
                public TracingRetriever(IRetriever inner) { }
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);
        Assert.DoesNotContain(diagnostics, static d => d.Severity == DiagnosticSeverity.Error);
        // Order=1 LoggingRetriever wraps RealRetriever, conditional on SomeOptions
        Assert.Contains("services.Any(d => d.ServiceType == typeof(global::SomeOptions))", output);
        Assert.Contains("new global::LoggingRetriever", output);
        Assert.Contains("GetService<global::ILogger>", output);         // optional dep
        // Order=2 TracingRetriever wraps unconditionally
        Assert.Contains("new global::TracingRetriever", output);
        // LoggingRetriever (Order=1, innermost) is registered before TracingRetriever (Order=2, outermost) wraps it
        Assert.True(output.IndexOf("LoggingRetriever") < output.IndexOf("TracingRetriever"));
    }

    [Fact]
    public void DecoratorOf_AllowMultiple_OneClassDecoratesTwoInterfaces_GeneratesBoth()
    {
        var source = """
            using ZeroAlloc.Inject;
            public interface IFoo { }
            public interface IBar { }
            [Transient]
            public class FooImpl : IFoo { }
            [Transient]
            public class BarImpl : IBar { }
            [DecoratorOf(typeof(IFoo))]
            [DecoratorOf(typeof(IBar))]
            public class MultiDecorator : IFoo, IBar
            {
                public MultiDecorator(IFoo foo, IBar bar) { }
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);
        Assert.DoesNotContain(diagnostics, static d => d.Severity == DiagnosticSeverity.Error);
        // MultiDecorator should appear in the IFoo chain
        Assert.Contains("new global::MultiDecorator", output);
    }
}
