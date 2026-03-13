using Microsoft.CodeAnalysis;

namespace ZeroInject.Tests.GeneratorTests;

public class DecoratorGeneratorTests
{
    [Fact]
    public void Decorator_NonGeneric_RegistrationExtension_WrapsInner()
    {
        var source = """
            using ZeroInject;
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
            using ZeroInject;
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
            using ZeroInject;
            public interface IFoo { }
            [Transient]
            public class FooImpl : IFoo { }
            """;

        var (output, _) = GeneratorTestHelper.RunGenerator(source);
        Assert.Contains("TryAddTransient<global::IFoo>", output);
    }
}
