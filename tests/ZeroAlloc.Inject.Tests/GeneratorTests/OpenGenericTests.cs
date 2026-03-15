using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Inject.Tests.GeneratorTests;

public class OpenGenericTests
{
    [Fact]
    public void OpenGeneric_GeneratesServiceDescriptor()
    {
        var source = """
            using ZeroAlloc.Inject;
            namespace TestApp;

            public interface IRepository<T> { }

            [Scoped]
            public class Repository<T> : IRepository<T> { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Contains("ServiceDescriptor.Scoped(typeof(global::TestApp.IRepository<>), typeof(global::TestApp.Repository<>))", output);
    }

    [Fact]
    public void OpenGeneric_WithAs_NarrowsRegistration()
    {
        var source = """
            using ZeroAlloc.Inject;
            namespace TestApp;

            public interface IReadRepo<T> { }
            public interface IWriteRepo<T> { }

            [Scoped(As = typeof(IReadRepo<>))]
            public class Repo<T> : IReadRepo<T>, IWriteRepo<T> { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Contains("typeof(global::TestApp.IReadRepo<>), typeof(global::TestApp.Repo<>)", output);
        Assert.DoesNotContain("IWriteRepo", output);
    }

    [Fact]
    public void OpenGeneric_DefaultTryAdd_UsesTryAdd()
    {
        var source = """
            using ZeroAlloc.Inject;
            namespace TestApp;

            public interface IRepository<T> { }

            [Scoped]
            public class Repository<T> : IRepository<T> { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Contains("services.TryAdd(ServiceDescriptor.Scoped(", output);
    }

    [Fact]
    public void MultiTypeParameter_GeneratesCorrectUnboundForm()
    {
        var source = """
            using ZeroAlloc.Inject;
            namespace TestApp;

            public interface IRepository<TKey, TValue> { }

            [Scoped]
            public class Repository<TKey, TValue> : IRepository<TKey, TValue> { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Contains("typeof(global::TestApp.IRepository<,>), typeof(global::TestApp.Repository<,>)", output);
    }

    [Fact]
    public void OpenGeneric_AllowMultiple_UsesAdd()
    {
        var source = """
            using ZeroAlloc.Inject;
            namespace TestApp;

            public interface IRepo<T> { }

            [Scoped(AllowMultiple = true)]
            public class Repo<T> : IRepo<T> { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Contains("services.Add(ServiceDescriptor.Scoped(", output);
        Assert.DoesNotContain("TryAdd", output);
    }

    [Fact]
    public void OpenGeneric_StandaloneContainer_EmitsExplicitClosedTypeEntry()
    {
        var source = """
            using ZeroAlloc.Inject;
            namespace TestApp;
            public interface IRepository<T> { }
            public class OrderContext { }
            [Transient]
            public class Repository<T> : IRepository<T>
            {
                public Repository(OrderContext ctx) { }
            }
            [Transient]
            public class OrderService
            {
                public OrderService(IRepository<OrderContext> repo) { }
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGeneratorWithContainer(source);
        Assert.DoesNotContain(diagnostics, static d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("typeof(global::TestApp.IRepository<global::TestApp.OrderContext>)", output);
        Assert.DoesNotContain("MakeGenericType", output);
        Assert.DoesNotContain("GetMethod(", output);
    }

    [Fact]
    public void OpenGeneric_StandaloneContainer_ContainsNoReflection()
    {
        var source = """
            using ZeroAlloc.Inject;
            namespace TestApp;
            public interface IRepository<T> { }
            public class Order { }
            [Transient]
            public class Repository<T> : IRepository<T> { }
            [Transient]
            public class OrderService
            {
                public OrderService(IRepository<Order> repo) { }
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGeneratorWithContainer(source);
        Assert.DoesNotContain(diagnostics, static d => d.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain("MakeGenericType", output);
        Assert.DoesNotContain("MakeGenericMethod", output);
        Assert.DoesNotContain("GetMethod(", output);
        Assert.DoesNotContain("Delegate.CreateDelegate", output);
        Assert.DoesNotContain("ConcurrentDictionary", output);
        Assert.DoesNotContain("GetGenericTypeDefinition", output);
    }

    [Fact]
    public void OpenGeneric_ChainedDependency_BothClosedTypesEmitted()
    {
        var source = """
            using ZeroAlloc.Inject;
            namespace TestApp;
            public interface IRepository<T> { }
            public interface IContext<T> { }
            public class Order { }
            [Transient]
            public class Repository<T> : IRepository<T>
            {
                public Repository(IContext<T> ctx) { }
            }
            [Transient]
            public class Context<T> : IContext<T> { }
            [Transient]
            public class OrderService
            {
                public OrderService(IRepository<Order> repo) { }
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGeneratorWithContainer(source);
        Assert.DoesNotContain(diagnostics, static d => d.Severity == DiagnosticSeverity.Error);
        // Both closed types discovered transitively via fixed-point iteration
        Assert.Contains("typeof(global::TestApp.IRepository<global::TestApp.Order>)", output);
        Assert.Contains("typeof(global::TestApp.IContext<global::TestApp.Order>)", output);
        Assert.DoesNotContain("MakeGenericType", output);
    }
}
