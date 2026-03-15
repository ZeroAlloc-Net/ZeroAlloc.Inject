namespace ZeroAlloc.Inject.Tests.GeneratorTests;

public class BasicRegistrationTests
{
    [Fact]
    public void NoAttributedClasses_GeneratesNothing()
    {
        var source = """
            namespace TestApp;
            public class PlainService { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.DoesNotContain("Error", diagnostics.AsEnumerable().Select(d => d.Severity.ToString()));
    }

    [Fact]
    public void TransientAttribute_GeneratesTryAddTransient()
    {
        var source = """
            using ZeroAlloc.Inject;
            namespace TestApp;

            public interface IMyService { }

            [Transient]
            public class MyService : IMyService { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.DoesNotContain("Error", diagnostics.AsEnumerable().Select(d => d.Severity.ToString()));
        Assert.Contains("TryAddTransient<global::TestApp.IMyService>(sp => new global::TestApp.MyService())", output);
        Assert.Contains("TryAddTransient(sp => new global::TestApp.MyService())", output);
    }

    [Fact]
    public void ScopedAttribute_GeneratesTryAddScoped()
    {
        var source = """
            using ZeroAlloc.Inject;
            namespace TestApp;

            public interface IRepo { }

            [Scoped]
            public class Repo : IRepo { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Contains("TryAddScoped<global::TestApp.IRepo>(sp => new global::TestApp.Repo())", output);
        Assert.Contains("TryAddScoped(sp => new global::TestApp.Repo())", output);
    }

    [Fact]
    public void SingletonAttribute_GeneratesTryAddSingleton()
    {
        var source = """
            using ZeroAlloc.Inject;
            namespace TestApp;

            public interface ICache { }

            [Singleton]
            public class Cache : ICache { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Contains("TryAddSingleton<global::TestApp.ICache>(sp => new global::TestApp.Cache())", output);
        Assert.Contains("TryAddSingleton(sp => new global::TestApp.Cache())", output);
    }

    [Fact]
    public void ConcreteOnly_RegistersConcreteType()
    {
        var source = """
            using ZeroAlloc.Inject;
            namespace TestApp;

            [Transient]
            public class PlainService { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Contains("TryAddTransient(sp => new global::TestApp.PlainService())", output);
    }

    [Fact]
    public void FilteredInterfaces_AreExcluded()
    {
        var source = """
            using ZeroAlloc.Inject;
            using System;
            namespace TestApp;

            public interface IMyService { }

            [Transient]
            public class MyService : IMyService, IDisposable
            {
                public void Dispose() { }
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Contains("TryAddTransient<global::TestApp.IMyService>(sp => new global::TestApp.MyService())", output);
        Assert.DoesNotContain("IDisposable", output);
    }

    [Fact]
    public void GeneratedMethod_ReturnsIServiceCollection()
    {
        var source = """
            using ZeroAlloc.Inject;
            namespace TestApp;

            [Transient]
            public class Svc { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Contains("public static IServiceCollection", output);
        Assert.Contains("return services;", output);
    }

    [Fact]
    public void KeyedService_GeneratesKeyedRegistration()
    {
        var source = """
            using ZeroAlloc.Inject;
            namespace TestApp;

            public interface ICache { }

            [Singleton(Key = "redis")]
            public class RedisCache : ICache { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Contains("TryAddKeyedSingleton<global::TestApp.ICache>(\"redis\", (sp, _) => new global::TestApp.RedisCache())", output);
        Assert.Contains("TryAddKeyedSingleton<global::TestApp.RedisCache>(\"redis\", (sp, _) => new global::TestApp.RedisCache())", output);
    }

    [Fact]
    public void AllowMultiple_GeneratesAddInsteadOfTryAdd()
    {
        var source = """
            using ZeroAlloc.Inject;
            namespace TestApp;

            public interface IJob { }

            [Transient(AllowMultiple = true)]
            public class MyJob : IJob { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Contains("services.AddTransient<global::TestApp.IJob>(sp => new global::TestApp.MyJob())", output);
        Assert.DoesNotContain("TryAdd", output);
    }

    [Fact]
    public void AsProperty_NarrowsRegistration()
    {
        var source = """
            using ZeroAlloc.Inject;
            namespace TestApp;

            public interface IFoo { }
            public interface IBar { }

            [Transient(As = typeof(IFoo))]
            public class MyService : IFoo, IBar { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Contains("TryAddTransient<global::TestApp.IFoo>(sp => new global::TestApp.MyService())", output);
        Assert.DoesNotContain("IBar", output);
        Assert.DoesNotContain("TryAddTransient(sp => new global::TestApp.MyService())", output);
    }

    [Fact]
    public void MultipleInterfaces_RegistersAll()
    {
        var source = """
            using ZeroAlloc.Inject;
            namespace TestApp;

            public interface IFoo { }
            public interface IBar { }
            public interface IBaz { }

            [Transient]
            public class MultiService : IFoo, IBar, IBaz { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Contains("TryAddTransient<global::TestApp.IFoo>(sp => new global::TestApp.MultiService())", output);
        Assert.Contains("TryAddTransient<global::TestApp.IBar>(sp => new global::TestApp.MultiService())", output);
        Assert.Contains("TryAddTransient<global::TestApp.IBaz>(sp => new global::TestApp.MultiService())", output);
        Assert.Contains("TryAddTransient(sp => new global::TestApp.MultiService())", output);
    }

    [Fact]
    public void MixedLifetimes_AllRegistered()
    {
        var source = """
            using ZeroAlloc.Inject;
            namespace TestApp;

            public interface IA { }
            public interface IB { }
            public interface IC { }

            [Transient]
            public class SvcA : IA { }

            [Scoped]
            public class SvcB : IB { }

            [Singleton]
            public class SvcC : IC { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Contains("TryAddTransient<global::TestApp.IA>(sp => new global::TestApp.SvcA())", output);
        Assert.Contains("TryAddScoped<global::TestApp.IB>(sp => new global::TestApp.SvcB())", output);
        Assert.Contains("TryAddSingleton<global::TestApp.IC>(sp => new global::TestApp.SvcC())", output);
    }

    [Fact]
    public void OnlyFilteredInterfaces_RegistersConcreteOnly()
    {
        var source = """
            using ZeroAlloc.Inject;
            using System;
            namespace TestApp;

            [Transient]
            public class MyService : IDisposable
            {
                public void Dispose() { }
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Contains("TryAddTransient(sp => new global::TestApp.MyService())", output);
        Assert.DoesNotContain("IDisposable", output);
    }

    [Fact]
    public void IEquatable_IsFiltered()
    {
        var source = """
            using ZeroAlloc.Inject;
            using System;
            namespace TestApp;

            public interface IMyService { }

            [Transient]
            public class MyService : IMyService, IEquatable<MyService>
            {
                public bool Equals(MyService other) => false;
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Contains("TryAddTransient<global::TestApp.IMyService>(sp => new global::TestApp.MyService())", output);
        Assert.DoesNotContain("IEquatable", output);
    }

    [Fact]
    public void KeyedPlusAllowMultiple_GeneratesAddKeyed()
    {
        var source = """
            using ZeroAlloc.Inject;
            namespace TestApp;

            public interface ICache { }

            [Singleton(Key = "redis", AllowMultiple = true)]
            public class RedisCache : ICache { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Contains("services.AddKeyedSingleton<global::TestApp.ICache>(\"redis\", (sp, _) => new global::TestApp.RedisCache())", output);
        Assert.DoesNotContain("TryAdd", output);
    }

    [Fact]
    public void KeyedPlusAs_NarrowsKeyedRegistration()
    {
        var source = """
            using ZeroAlloc.Inject;
            namespace TestApp;

            public interface IRead { }
            public interface IWrite { }

            [Scoped(Key = "main", As = typeof(IRead))]
            public class Store : IRead, IWrite { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Contains("TryAddKeyedScoped<global::TestApp.IRead>(\"main\", (sp, _) => new global::TestApp.Store())", output);
        Assert.DoesNotContain("IWrite", output);
    }

    [Fact]
    public void AsProperty_StillRegistersConcreteType()
    {
        var source = """
            using ZeroAlloc.Inject;
            namespace TestApp;

            public interface IFoo { }
            public interface IBar { }

            [Transient(As = typeof(IFoo))]
            public class Foo : IFoo, IBar { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Contains("TryAddTransient<global::TestApp.IFoo>(sp => new global::TestApp.Foo())", output);
        Assert.DoesNotContain("IBar", output);
    }
}
