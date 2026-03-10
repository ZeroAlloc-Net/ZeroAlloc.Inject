namespace ZeroInject.Tests.GeneratorTests;

public class OpenGenericTests
{
    [Fact]
    public void OpenGeneric_GeneratesServiceDescriptor()
    {
        var source = """
            using ZeroInject;
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
            using ZeroInject;
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
            using ZeroInject;
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
            using ZeroInject;
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
            using ZeroInject;
            namespace TestApp;

            public interface IRepo<T> { }

            [Scoped(AllowMultiple = true)]
            public class Repo<T> : IRepo<T> { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Contains("services.Add(ServiceDescriptor.Scoped(", output);
        Assert.DoesNotContain("TryAdd", output);
    }
}
