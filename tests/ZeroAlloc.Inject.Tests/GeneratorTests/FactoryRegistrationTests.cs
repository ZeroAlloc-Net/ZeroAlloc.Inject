using System;

namespace ZeroAlloc.Inject.Tests.GeneratorTests;

public class FactoryRegistrationTests
{
    [Fact]
    public void ParameterlessConstructor_GeneratesFactoryLambda()
    {
        var source = """
            using ZeroAlloc.Inject;
            namespace TestApp;

            public interface IMyService { }

            [Transient]
            public class MyService : IMyService { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Contains("sp => new global::TestApp.MyService()", output);
    }

    [Fact]
    public void SingleParameter_GeneratesGetRequiredService()
    {
        var source = """
            using ZeroAlloc.Inject;
            namespace TestApp;

            public interface IMyService { }
            public interface ILogger { }

            [Transient]
            public class MyService : IMyService
            {
                public MyService(ILogger logger) { }
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Contains("sp => new global::TestApp.MyService(", output);
        Assert.Contains("sp.GetRequiredService<global::TestApp.ILogger>()", output);
    }

    [Fact]
    public void OptionalParameter_GeneratesGetService()
    {
        var source = """
            using ZeroAlloc.Inject;
            namespace TestApp;

            public interface IMyService { }
            public interface ILogger { }

            [Transient]
            public class MyService : IMyService
            {
                public MyService(ILogger? logger = null) { }
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Contains("sp.GetService<global::TestApp.ILogger>()", output);
        Assert.DoesNotContain("GetRequiredService", output);
    }

    [Fact]
    public void MultipleConstructors_WithoutAttribute_ProducesZAI009()
    {
        var source = """
            using ZeroAlloc.Inject;
            namespace TestApp;

            public interface IMyService { }

            [Transient]
            public class MyService : IMyService
            {
                public MyService() { }
                public MyService(int x) { }
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Contains(diagnostics, d => string.Equals(d.Id, "ZAI009", StringComparison.Ordinal));
    }

    [Fact]
    public void PrimitiveParameter_String_ProducesZAI010()
    {
        var source = """
            using ZeroAlloc.Inject;
            namespace TestApp;

            public interface IMyService { }

            [Transient]
            public class MyService : IMyService
            {
                public MyService(string name) { }
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Contains(diagnostics, d => string.Equals(d.Id, "ZAI010", StringComparison.Ordinal));
    }

    [Fact]
    public void PrimitiveParameter_Int_ProducesZAI010()
    {
        var source = """
            using ZeroAlloc.Inject;
            namespace TestApp;

            public interface IMyService { }

            [Transient]
            public class MyService : IMyService
            {
                public MyService(int count) { }
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Contains(diagnostics, d => string.Equals(d.Id, "ZAI010", StringComparison.Ordinal));
    }

    [Fact]
    public void ConcreteOnly_GeneratesFactoryLambda()
    {
        var source = """
            using ZeroAlloc.Inject;
            namespace TestApp;

            [Transient]
            public class PlainService { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Contains("sp => new global::TestApp.PlainService()", output);
    }

    [Fact]
    public void OpenGeneric_StillUsesServiceDescriptor()
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
        Assert.DoesNotContain("sp =>", output);
    }

    [Fact]
    public void MultipleParameters_GeneratesAllGetRequiredService()
    {
        var source = """
            using ZeroAlloc.Inject;
            namespace TestApp;

            public interface IMyService { }
            public interface IRepo { }
            public interface ILogger { }

            [Transient]
            public class MyService : IMyService
            {
                public MyService(IRepo repo, ILogger logger) { }
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Contains("sp.GetRequiredService<global::TestApp.IRepo>()", output);
        Assert.Contains("sp.GetRequiredService<global::TestApp.ILogger>()", output);
    }

    [Fact]
    public void MixedRequiredAndOptionalParameters()
    {
        var source = """
            using ZeroAlloc.Inject;
            namespace TestApp;

            public interface IMyService { }
            public interface IRepo { }
            public interface ILogger { }

            [Transient]
            public class MyService : IMyService
            {
                public MyService(IRepo repo, ILogger? logger = null) { }
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Contains("sp.GetRequiredService<global::TestApp.IRepo>()", output);
        Assert.Contains("sp.GetService<global::TestApp.ILogger>()", output);
    }

    [Fact]
    public void KeyedService_GeneratesKeyedFactoryLambda()
    {
        var source = """
            using ZeroAlloc.Inject;
            namespace TestApp;

            public interface ICache { }

            [Singleton(Key = "redis")]
            public class RedisCache : ICache { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Contains("(sp, _) => new global::TestApp.RedisCache()", output);
        Assert.Contains("\"redis\"", output);
    }

    [Fact]
    public void KeyedService_WithParameters_GeneratesKeyedFactoryLambda()
    {
        var source = """
            using ZeroAlloc.Inject;
            namespace TestApp;

            public interface ICache { }
            public interface ISerializer { }

            [Singleton(Key = "redis")]
            public class RedisCache : ICache
            {
                public RedisCache(ISerializer serializer) { }
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Contains("(sp, _) => new global::TestApp.RedisCache(", output);
        Assert.Contains("sp.GetRequiredService<global::TestApp.ISerializer>()", output);
    }

    [Fact]
    public void MultipleConstructors_WithActivatorUtilitiesConstructor_UsesMarked()
    {
        var source = """
            using ZeroAlloc.Inject;
            namespace TestApp;

            namespace Microsoft.Extensions.DependencyInjection
            {
                [System.AttributeUsage(System.AttributeTargets.Constructor)]
                public class ActivatorUtilitiesConstructorAttribute : System.Attribute { }
            }

            public interface IMyService { }
            public interface IRepo { }

            [Transient]
            public class MyService : IMyService
            {
                public MyService() { }

                [Microsoft.Extensions.DependencyInjection.ActivatorUtilitiesConstructor]
                public MyService(IRepo repo) { }
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Contains("sp.GetRequiredService<global::TestApp.IRepo>()", output);
        Assert.DoesNotContain(diagnostics, d => string.Equals(d.Id, "ZAI009", StringComparison.Ordinal));
    }

    [Fact]
    public void ConcreteOnly_WithParameters_GeneratesFactoryLambda()
    {
        var source = """
            using ZeroAlloc.Inject;
            namespace TestApp;

            public interface ILogger { }

            [Transient]
            public class PlainService
            {
                public PlainService(ILogger logger) { }
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Contains("TryAddTransient(sp => new global::TestApp.PlainService(", output);
        Assert.Contains("sp.GetRequiredService<global::TestApp.ILogger>()", output);
    }

    [Fact]
    public void AsProperty_WithParameters_GeneratesFactoryLambda()
    {
        var source = """
            using ZeroAlloc.Inject;
            namespace TestApp;

            public interface IFoo { }
            public interface IBar { }
            public interface ILogger { }

            [Transient(As = typeof(IFoo))]
            public class MyService : IFoo, IBar
            {
                public MyService(ILogger logger) { }
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Contains("TryAddTransient<global::TestApp.IFoo>(sp => new global::TestApp.MyService(", output);
        Assert.Contains("sp.GetRequiredService<global::TestApp.ILogger>()", output);
        Assert.DoesNotContain("IBar", output);
    }

    [Fact]
    public void InterfaceParameter_DoesNotProduceZAI010()
    {
        var source = """
            using ZeroAlloc.Inject;
            namespace TestApp;

            public interface IMyService { }
            public interface IRepo { }

            [Transient]
            public class MyService : IMyService
            {
                public MyService(IRepo repo) { }
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => string.Equals(d.Id, "ZAI010", StringComparison.Ordinal));
    }

    [Fact]
    public void ClassParameter_DoesNotProduceZAI010()
    {
        var source = """
            using ZeroAlloc.Inject;
            namespace TestApp;

            public interface IMyService { }
            public class SomeDependency { }

            [Transient]
            public class MyService : IMyService
            {
                public MyService(SomeDependency dep) { }
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => string.Equals(d.Id, "ZAI010", StringComparison.Ordinal));
        Assert.Contains("sp.GetRequiredService<global::TestApp.SomeDependency>()", output);
    }

    [Fact]
    public void PrimitiveParameter_Bool_ProducesZAI010()
    {
        var source = """
            using ZeroAlloc.Inject;
            namespace TestApp;

            public interface IMyService { }

            [Transient]
            public class MyService : IMyService
            {
                public MyService(bool enabled) { }
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Contains(diagnostics, d => string.Equals(d.Id, "ZAI010", StringComparison.Ordinal));
    }

    [Fact]
    public void PrimitiveParameter_Enum_ProducesZAI010()
    {
        var source = """
            using ZeroAlloc.Inject;
            namespace TestApp;

            public interface IMyService { }
            public enum Mode { Fast, Slow }

            [Transient]
            public class MyService : IMyService
            {
                public MyService(Mode mode) { }
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Contains(diagnostics, d => string.Equals(d.Id, "ZAI010", StringComparison.Ordinal));
    }

    [Fact]
    public void PrimitiveParameter_Struct_ProducesZAI010()
    {
        var source = """
            using ZeroAlloc.Inject;
            namespace TestApp;

            public interface IMyService { }
            public struct Config { public int Value; }

            [Transient]
            public class MyService : IMyService
            {
                public MyService(Config config) { }
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Contains(diagnostics, d => string.Equals(d.Id, "ZAI010", StringComparison.Ordinal));
    }

    [Fact]
    public void KeyedService_WithOptionalParameter_GeneratesGetService()
    {
        var source = """
            using ZeroAlloc.Inject;
            namespace TestApp;

            public interface ICache { }
            public interface ISerializer { }

            [Singleton(Key = "redis")]
            public class RedisCache : ICache
            {
                public RedisCache(ISerializer? serializer = null) { }
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Contains("(sp, _) => new global::TestApp.RedisCache(", output);
        Assert.Contains("sp.GetService<global::TestApp.ISerializer>()", output);
        Assert.DoesNotContain("GetRequiredService", output);
    }
}
