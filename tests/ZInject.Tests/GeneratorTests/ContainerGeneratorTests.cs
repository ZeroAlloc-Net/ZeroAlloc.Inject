namespace ZInject.Tests.GeneratorTests;

public class ContainerGeneratorTests
{
    // --- Task 5: Detection ---

    [Fact]
    public void WhenContainerReferenced_GeneratesServiceProvider()
    {
        var source = """
            using ZInject;
            namespace TestApp;

            public interface IFoo { }

            [Transient]
            public class Foo : IFoo { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGeneratorWithContainer(source);

        Assert.Contains("TestAssemblyServiceProvider", output);
        Assert.Contains("ZInjectServiceProviderBase", output);
    }

    [Fact]
    public void WhenContainerNotReferenced_DoesNotGenerateServiceProvider()
    {
        var source = """
            using ZInject;
            namespace TestApp;

            public interface IFoo { }

            [Transient]
            public class Foo : IFoo { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.DoesNotContain("ServiceProvider", output);
        Assert.DoesNotContain("ResolveKnown", output);
    }

    // --- Task 6: Transient resolution ---

    [Fact]
    public void Transient_Parameterless_GeneratesTypeCheckAndNew()
    {
        var source = """
            using ZInject;
            namespace TestApp;

            public interface IFoo { }

            [Transient]
            public class Foo : IFoo { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGeneratorWithContainer(source);

        Assert.Contains("if (serviceType == typeof(global::TestApp.IFoo))", output);
        Assert.Contains("return new global::TestApp.Foo();", output);
        // Also concrete type check
        Assert.Contains("if (serviceType == typeof(global::TestApp.Foo))", output);
    }

    [Fact]
    public void Transient_WithDependencies_GeneratesGetServiceCalls()
    {
        var source = """
            using ZInject;
            namespace TestApp;

            public interface IFoo { }
            public interface IBar { }

            [Transient]
            public class Foo : IFoo { }

            [Transient]
            public class Bar : IBar
            {
                public Bar(IFoo foo) { }
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGeneratorWithContainer(source);

        Assert.Contains("(global::TestApp.IFoo)GetService(typeof(global::TestApp.IFoo))!", output);
        Assert.Contains("new global::TestApp.Bar(", output);
    }

    [Fact]
    public void Transient_OptionalParameter_GeneratesNullableGetService()
    {
        var source = """
            using ZInject;
            namespace TestApp;

            public interface IFoo { }
            public interface IOptional { }

            [Transient]
            public class Foo : IFoo
            {
                public Foo(IOptional? opt = null) { }
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGeneratorWithContainer(source);

        Assert.Contains("(global::TestApp.IOptional?)GetService(typeof(global::TestApp.IOptional))", output);
        Assert.DoesNotContain("GetService(typeof(global::TestApp.IOptional))!", output);
    }

    [Fact]
    public void Transient_AllInterfaces_Registered()
    {
        var source = """
            using ZInject;
            namespace TestApp;

            public interface IFoo { }
            public interface IBar { }

            [Transient]
            public class Multi : IFoo, IBar { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGeneratorWithContainer(source);

        Assert.Contains("if (serviceType == typeof(global::TestApp.IFoo))", output);
        Assert.Contains("if (serviceType == typeof(global::TestApp.IBar))", output);
        Assert.Contains("if (serviceType == typeof(global::TestApp.Multi))", output);
    }

    [Fact]
    public void Transient_AsProperty_NarrowsRegistration()
    {
        var source = """
            using ZInject;
            namespace TestApp;

            public interface IFoo { }
            public interface IBar { }

            [Transient(As = typeof(IFoo))]
            public class Svc : IFoo, IBar { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGeneratorWithContainer(source);

        Assert.Contains("if (serviceType == typeof(global::TestApp.IFoo))", output);
        Assert.DoesNotContain("typeof(global::TestApp.IBar)", output);
        // When As is set, no concrete type check
        Assert.DoesNotContain("typeof(global::TestApp.Svc)", output);
    }

    // --- Task 7: Singleton resolution ---

    [Fact]
    public void Singleton_GeneratesFieldDeclaration()
    {
        var source = """
            using ZInject;
            namespace TestApp;

            public interface ICache { }

            [Singleton]
            public class Cache : ICache { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGeneratorWithContainer(source);

        Assert.Contains("private global::TestApp.Cache? _singleton_0;", output);
    }

    [Fact]
    public void Singleton_GeneratesInterlockedCompareExchangePattern()
    {
        var source = """
            using ZInject;
            namespace TestApp;

            public interface ICache { }

            [Singleton]
            public class Cache : ICache { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGeneratorWithContainer(source);

        Assert.Contains("if (_singleton_0 != null) return _singleton_0;", output);
        Assert.Contains("var instance = new global::TestApp.Cache();", output);
        Assert.Contains("Interlocked.CompareExchange(ref _singleton_0, instance, null) ?? _singleton_0;", output);
    }

    [Fact]
    public void Singleton_WithDependencies_GeneratesGetServiceInNew()
    {
        var source = """
            using ZInject;
            namespace TestApp;

            public interface ICache { }
            public interface ISerializer { }

            [Singleton]
            public class Cache : ICache
            {
                public Cache(ISerializer ser) { }
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGeneratorWithContainer(source);

        Assert.Contains("var instance = new global::TestApp.Cache((global::TestApp.ISerializer)GetService(typeof(global::TestApp.ISerializer))!);", output);
    }

    [Fact]
    public void Singleton_MultipleInterfaces_AllTypeChecksShareField()
    {
        var source = """
            using ZInject;
            namespace TestApp;

            public interface ICache { }
            public interface IStore { }

            [Singleton]
            public class Cache : ICache, IStore { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGeneratorWithContainer(source);

        // All interface checks + concrete check should use _singleton_0
        Assert.Contains("if (serviceType == typeof(global::TestApp.ICache))", output);
        Assert.Contains("if (serviceType == typeof(global::TestApp.IStore))", output);
        Assert.Contains("if (serviceType == typeof(global::TestApp.Cache))", output);
        // All should reference the same field
        Assert.Contains("Interlocked.CompareExchange(ref _singleton_0", output);
    }

    // --- Task 8: Scope class ---

    [Fact]
    public void Scoped_GeneratesFieldInScopeClass()
    {
        var source = """
            using ZInject;
            namespace TestApp;

            public interface IRepo { }

            [Scoped]
            public class Repo : IRepo { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGeneratorWithContainer(source);

        Assert.Contains("private global::TestApp.Repo? _scoped_0;", output);
    }

    [Fact]
    public void ScopeClass_IsNestedSealedClass()
    {
        var source = """
            using ZInject;
            namespace TestApp;

            public interface IRepo { }

            [Scoped]
            public class Repo : IRepo { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGeneratorWithContainer(source);

        Assert.Contains("private sealed class Scope : global::ZInject.Container.ZInjectScope", output);
    }

    [Fact]
    public void Scope_TransientsCreateFreshInstances()
    {
        var source = """
            using ZInject;
            namespace TestApp;

            public interface IFoo { }

            [Transient]
            public class Foo : IFoo { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGeneratorWithContainer(source);

        // The ResolveScopedKnown method should also have transient type checks
        // Check that both root ResolveKnown and scope ResolveScopedKnown contain the transient
        var scopeSection = output.Substring(output.IndexOf("ResolveScopedKnown"));
        Assert.Contains("return new global::TestApp.Foo();", scopeSection);
    }

    [Fact]
    public void Scope_SingletonsDelegateToRoot()
    {
        var source = """
            using ZInject;
            namespace TestApp;

            public interface ICache { }

            [Singleton]
            public class Cache : ICache { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGeneratorWithContainer(source);

        var scopeSection = output.Substring(output.IndexOf("ResolveScopedKnown"));
        Assert.Contains("return Root.GetService(serviceType);", scopeSection);
    }

    [Fact]
    public void Scope_ScopedServiceUsesLazyInit()
    {
        var source = """
            using ZInject;
            namespace TestApp;

            public interface IRepo { }

            [Scoped]
            public class Repo : IRepo { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGeneratorWithContainer(source);

        var scopeSection = output.Substring(output.IndexOf("ResolveScopedKnown"));
        Assert.Contains("if (_scoped_0 == null) _scoped_0 = new global::TestApp.Repo();", scopeSection);
        Assert.Contains("return _scoped_0;", scopeSection);
    }

    [Fact]
    public void Scope_ScopedNotResolvedFromRoot()
    {
        var source = """
            using ZInject;
            namespace TestApp;

            public interface IRepo { }

            [Scoped]
            public class Repo : IRepo { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGeneratorWithContainer(source);

        // Root ResolveKnown should NOT contain scoped service type checks
        var resolveKnownStart = output.IndexOf("protected override object? ResolveKnown(Type serviceType)");
        var resolveKnownEnd = output.IndexOf("override bool IsKnownService");
        var rootSection = output.Substring(resolveKnownStart, resolveKnownEnd - resolveKnownStart);
        Assert.DoesNotContain("typeof(global::TestApp.IRepo)", rootSection);
    }

    [Fact]
    public void MixedLifetimes_AllCorrectlyPlaced()
    {
        var source = """
            using ZInject;
            namespace TestApp;

            public interface IFoo { }
            public interface ICache { }
            public interface IRepo { }

            [Transient]
            public class Foo : IFoo { }

            [Singleton]
            public class Cache : ICache { }

            [Scoped]
            public class Repo : IRepo { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGeneratorWithContainer(source);

        // Root has transient + singleton, not scoped
        var resolveKnownStart = output.IndexOf("protected override object? ResolveKnown(Type serviceType)");
        var resolveKnownEnd = output.IndexOf("override bool IsKnownService");
        var rootSection = output.Substring(resolveKnownStart, resolveKnownEnd - resolveKnownStart);

        Assert.Contains("typeof(global::TestApp.IFoo)", rootSection);
        Assert.Contains("typeof(global::TestApp.ICache)", rootSection);
        Assert.DoesNotContain("typeof(global::TestApp.IRepo)", rootSection);

        // Scope has all three
        var scopeSection = output.Substring(output.IndexOf("ResolveScopedKnown"));
        Assert.Contains("typeof(global::TestApp.IFoo)", scopeSection);
        Assert.Contains("typeof(global::TestApp.ICache)", scopeSection);
        Assert.Contains("typeof(global::TestApp.IRepo)", scopeSection);
    }

    // --- Task 9: BuildZInjectServiceProvider extension method ---

    [Fact]
    public void GeneratesBuildExtensionMethod()
    {
        var source = """
            using ZInject;
            namespace TestApp;
            [Transient]
            public class Svc { }
            """;
        var (output, _) = GeneratorTestHelper.RunGeneratorWithContainer(source);
        Assert.Contains("BuildZInjectServiceProvider", output);
        Assert.Contains("this IServiceCollection services", output);
        Assert.Contains("BuildServiceProvider()", output);
    }

    // --- Task 10: ZInjectServiceProviderFactory ---

    [Fact]
    public void GeneratesServiceProviderFactory()
    {
        var source = """
            using ZInject;
            namespace TestApp;
            [Transient]
            public class Svc { }
            """;
        var (output, _) = GeneratorTestHelper.RunGeneratorWithContainer(source);
        Assert.Contains("class ZInjectServiceProviderFactory", output);
        Assert.Contains("IServiceProviderFactory<IServiceCollection>", output);
        Assert.Contains("CreateBuilder", output);
        Assert.Contains("CreateServiceProvider", output);
    }

    // --- Task 11: Keyed services ---

    [Fact]
    public void KeyedService_GeneratesKeyedResolution()
    {
        var source = """
            using ZInject;
            namespace TestApp;
            public interface ICache { }
            [Singleton(Key = "redis")]
            public class RedisCache : ICache { }
            """;
        var (output, _) = GeneratorTestHelper.RunGeneratorWithContainer(source);
        Assert.Contains("IKeyedServiceProvider", output);
        Assert.Contains("\"redis\"", output);
    }

    [Fact]
    public void KeyedSingleton_GeneratesCachedField()
    {
        var source = """
            using ZInject;
            namespace TestApp;
            public interface ICache { }
            [Singleton(Key = "redis")]
            public class RedisCache : ICache { }
            """;
        var (output, _) = GeneratorTestHelper.RunGeneratorWithContainer(source);
        Assert.Contains("_keyedSingleton_", output);
        Assert.Contains("Interlocked.CompareExchange", output);
    }

    [Fact]
    public void KeyedTransient_InScope_CreatesNewInstance()
    {
        var source = """
            using ZInject;
            namespace TestApp;
            public interface ICache { }
            [Transient(Key = "fast")]
            public class FastCache : ICache { }
            """;
        var (output, _) = GeneratorTestHelper.RunGeneratorWithContainer(source);
        Assert.Contains("IKeyedServiceProvider", output);
        Assert.Contains("\"fast\"", output);
    }

    // --- Task 13: Disposable tracking ---

    [Fact]
    public void DisposableTransient_InScope_GeneratesTrackDisposable()
    {
        var source = """
            using ZInject;
            using System;
            namespace TestApp;
            public interface IFoo : IDisposable { }
            [Transient]
            public class Foo : IFoo { public void Dispose() { } }
            """;
        var (output, _) = GeneratorTestHelper.RunGeneratorWithContainer(source);
        // In scope, transients that implement IDisposable should be tracked
        var scopeSection = output.Substring(output.IndexOf("ResolveScopedKnown"));
        Assert.Contains("TrackDisposable", scopeSection);
    }

    [Fact]
    public void NonDisposableTransient_InScope_NoTrackDisposable()
    {
        var source = """
            using ZInject;
            namespace TestApp;
            public interface IFoo { }
            [Transient]
            public class Foo : IFoo { }
            """;
        var (output, _) = GeneratorTestHelper.RunGeneratorWithContainer(source);
        Assert.DoesNotContain("TrackDisposable", output);
    }

    [Fact]
    public void KeyedService_NotInResolveKnown()
    {
        var source = """
            using ZInject;
            namespace TestApp;
            public interface ICache { }
            [Singleton(Key = "redis")]
            public class RedisCache : ICache { }
            """;
        var (output, _) = GeneratorTestHelper.RunGeneratorWithContainer(source);
        // Keyed services should NOT appear in ResolveKnown
        var resolveKnownStart = output.IndexOf("protected override object? ResolveKnown(Type serviceType)");
        var resolveKnownEnd = output.IndexOf("protected override bool IsKnownService");
        var rootSection = output.Substring(resolveKnownStart, resolveKnownEnd - resolveKnownStart);
        Assert.DoesNotContain("typeof(global::TestApp.ICache)", rootSection);
    }

    [Fact]
    public void KeyedScopedService_GeneratesScopedFieldAndLazyInit()
    {
        var source = """
            using ZInject;
            namespace TestApp;
            public interface IRepo { }
            [Scoped(Key = "primary")]
            public class PrimaryRepo : IRepo { }
            """;
        var (output, _) = GeneratorTestHelper.RunGeneratorWithContainer(source);
        // Should have a keyed scoped field
        Assert.Contains("_keyedScoped_0", output);
        // Should have lazy-init pattern in scope's GetKeyedService
        var scopeKeyedSection = output.Substring(output.IndexOf("public object? GetKeyedService"));
        Assert.Contains("if (_keyedScoped_0 == null) _keyedScoped_0 = new global::TestApp.PrimaryRepo();", scopeKeyedSection);
        Assert.Contains("return _keyedScoped_0;", scopeKeyedSection);
    }

    [Fact]
    public void KeyedDisposableTransient_InScope_GeneratesTrackDisposable()
    {
        var source = """
            using ZInject;
            using System;
            namespace TestApp;
            public interface ICache : IDisposable { }
            [Transient(Key = "fast")]
            public class FastCache : ICache { public void Dispose() { } }
            """;
        var (output, _) = GeneratorTestHelper.RunGeneratorWithContainer(source);
        // In scope's GetKeyedService, disposable transients should be tracked
        var scopeKeyedSection = output.Substring(output.IndexOf("public object? GetKeyedService"));
        Assert.Contains("TrackDisposable", scopeKeyedSection);
    }

    [Fact]
    public void CreateScopeCore_IsOverridden()
    {
        var source = """
            using ZInject;
            namespace TestApp;

            public interface IFoo { }

            [Transient]
            public class Foo : IFoo { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGeneratorWithContainer(source);

        Assert.Contains("protected override global::ZInject.Container.ZInjectScope CreateScopeCore(IServiceScope fallbackScope)", output);
        Assert.Contains("return new Scope(this, fallbackScope);", output);
    }

    // --- Phase 3 gap-fill tests ---

    [Fact]
    public void MultipleKeyedServices_SameInterface_DifferentKeys()
    {
        var source = """
            using ZInject;
            namespace TestApp;
            public interface ICache { }
            [Singleton(Key = "redis")]
            public class RedisCache : ICache { }
            [Singleton(Key = "memory")]
            public class MemoryCache : ICache { }
            """;
        var (output, _) = GeneratorTestHelper.RunGeneratorWithContainer(source);
        Assert.Contains("\"redis\"", output);
        Assert.Contains("\"memory\"", output);
        Assert.Contains("_keyedSingleton_0", output);
        Assert.Contains("_keyedSingleton_1", output);
    }

    [Fact]
    public void KeyedService_WithDependencies_GeneratesGetServiceCalls()
    {
        var source = """
            using ZInject;
            namespace TestApp;
            public interface ISerializer { }
            [Transient]
            public class JsonSerializer : ISerializer { }
            public interface ICache { }
            [Singleton(Key = "redis")]
            public class RedisCache : ICache
            {
                public RedisCache(ISerializer serializer) { }
            }
            """;
        var (output, _) = GeneratorTestHelper.RunGeneratorWithContainer(source);
        // The keyed singleton should have dependency resolution in its new expression
        Assert.Contains("new global::TestApp.RedisCache((global::TestApp.ISerializer)GetService(typeof(global::TestApp.ISerializer))!)", output);
    }

    [Fact]
    public void MixedKeyedAndNonKeyed_SameInterface()
    {
        var source = """
            using ZInject;
            namespace TestApp;
            public interface ICache { }
            [Singleton]
            public class DefaultCache : ICache { }
            [Singleton(Key = "redis")]
            public class RedisCache : ICache { }
            """;
        var (output, _) = GeneratorTestHelper.RunGeneratorWithContainer(source);
        // Non-keyed should be in ResolveKnown
        var resolveKnownStart = output.IndexOf("protected override object? ResolveKnown");
        var keyedStart = output.IndexOf("public object? GetKeyedService");
        var resolveKnown = output.Substring(resolveKnownStart, keyedStart - resolveKnownStart);
        Assert.Contains("typeof(global::TestApp.ICache)", resolveKnown); // DefaultCache

        // Keyed should be in GetKeyedService
        var keyedSection = output.Substring(keyedStart);
        Assert.Contains("\"redis\"", keyedSection);
    }

    // --- Blindspot fixes ---

    [Fact]
    public void ScopedDisposable_GeneratesTrackDisposable()
    {
        var source = """
            using ZInject;
            using System;
            namespace TestApp;

            public interface IRepo { }

            [Scoped]
            public class Repo : IRepo, IDisposable
            {
                public void Dispose() { }
            }
            """;

        var (output, _) = GeneratorTestHelper.RunGeneratorWithContainer(source);
        Assert.Contains("TrackDisposable(", output);
    }

    [Fact]
    public void ScopedNonDisposable_DoesNotGenerateTrackDisposable()
    {
        var source = """
            using ZInject;
            namespace TestApp;

            public interface IRepo { }

            [Scoped]
            public class Repo : IRepo { }
            """;

        var (output, _) = GeneratorTestHelper.RunGeneratorWithContainer(source);
        // Only scoped, no transients — TrackDisposable should not appear in scoped init
        var scopedSection = output.Substring(output.IndexOf("ResolveScopedKnown"));
        Assert.DoesNotContain("TrackDisposable(", scopedSection);
    }

    [Fact]
    public void DisposableSingleton_GeneratesRaceDisposalGuard()
    {
        var source = """
            using ZInject;
            using System;
            namespace TestApp;

            public interface ICache { }

            [Singleton]
            public class Cache : ICache, IDisposable
            {
                public void Dispose() { }
            }
            """;

        var (output, _) = GeneratorTestHelper.RunGeneratorWithContainer(source);
        Assert.Contains("(instance as System.IDisposable)?.Dispose()", output);
        Assert.Contains("var existing = Interlocked.CompareExchange", output);
    }

    [Fact]
    public void NonDisposableSingleton_DoesNotGenerateRaceDisposalGuard()
    {
        var source = """
            using ZInject;
            namespace TestApp;

            public interface ICache { }

            [Singleton]
            public class Cache : ICache { }
            """;

        var (output, _) = GeneratorTestHelper.RunGeneratorWithContainer(source);
        Assert.DoesNotContain("(instance as System.IDisposable)?.Dispose()", output);
    }

    // --- IEnumerable<T> support ---

    [Fact]
    public void IEnumerable_SingleTransient_GeneratesArrayInResolveKnown()
    {
        var source = """
            using ZInject;
            namespace TestApp;

            public interface IFoo { }

            [Transient]
            public class Foo : IFoo { }
            """;

        var (output, _) = GeneratorTestHelper.RunGeneratorWithContainer(source);
        Assert.Contains("IEnumerable<global::TestApp.IFoo>", output);
        Assert.Contains("new global::TestApp.IFoo[]", output);
    }

    [Fact]
    public void IEnumerable_InScope_IncludesAllLifetimes()
    {
        var source = """
            using ZInject;
            namespace TestApp;

            public interface IHandler { }

            [Transient(AllowMultiple = true)]
            public class HandlerA : IHandler { }

            [Singleton(AllowMultiple = true)]
            public class HandlerB : IHandler { }

            [Scoped(AllowMultiple = true)]
            public class HandlerC : IHandler { }
            """;

        var (output, _) = GeneratorTestHelper.RunGeneratorWithContainer(source);
        // Find the scope's ResolveScopedKnown section
        var scopeSection = output.Substring(output.IndexOf("ResolveScopedKnown"));
        Assert.Contains("IEnumerable<global::TestApp.IHandler>", scopeSection);
        // All three should be in the array
        Assert.Contains("HandlerA", scopeSection);
        Assert.Contains("HandlerB", scopeSection);
        Assert.Contains("HandlerC", scopeSection);
    }

    [Fact]
    public void KeyedServices_GeneratesIKeyedServiceProviderSelfResolution()
    {
        var source = """
            using ZInject;
            namespace TestApp;

            public interface ICache { }

            [Singleton(Key = "redis")]
            public class RedisCache : ICache { }
            """;

        var (output, _) = GeneratorTestHelper.RunGeneratorWithContainer(source);
        Assert.Contains("typeof(IKeyedServiceProvider)", output);
        Assert.Contains("return this;", output);
    }

    // --- Task 2: IServiceProviderIsService — IsKnownService ---

    [Fact]
    public void Hybrid_IsKnownService_EmitsTypeChecks()
    {
        var source = """
            using ZInject;
            public interface IFoo { }
            [Transient]
            public class FooImpl : IFoo { }
            """;

        var (output, _) = GeneratorTestHelper.RunGeneratorWithContainer(source);
        Assert.Contains("override bool IsKnownService", output);
        Assert.Contains("typeof(global::IFoo)", output);
        Assert.Contains("typeof(global::FooImpl)", output);
    }

    [Fact]
    public void Standalone_IsKnownService_EmitsTypeChecks()
    {
        var source = """
            using ZInject;
            public interface IFoo { }
            [Transient]
            public class FooImpl : IFoo { }
            """;

        var (output, _) = GeneratorTestHelper.RunGeneratorWithContainer(source);
        Assert.Contains("override bool IsKnownService", output);
    }

    [Fact]
    public void IsKnownService_OpenGeneric_EmitsExplicitClosedTypeCheck()
    {
        // After reflection removal, IsKnownService emits explicit typeof(IRepo<string>) == serviceType
        // entries for closed types detected via constructor parameter analysis.
        var source = """
            using ZInject;
            public interface IRepo<T> { }
            [Transient]
            public class Repo<T> : IRepo<T> { }
            [Transient]
            public class Consumer
            {
                public Consumer(IRepo<string> repo) { }
            }
            """;

        var (output, _) = GeneratorTestHelper.RunGeneratorWithContainer(source);
        Assert.Contains("IsKnownService", output);
        Assert.Contains("typeof(global::IRepo<string>)", output);
        Assert.DoesNotContain("GetGenericTypeDefinition", output);
    }

    // --- Task 6 (additional): IEnumerable<T> edge cases ---

    [Fact]
    public void IEnumerable_Singleton_DelegatesToGetService()
    {
        var source = """
            using ZInject;
            namespace TestApp;

            public interface ICache { }

            [Singleton]
            public class Cache : ICache { }
            """;

        var (output, _) = GeneratorTestHelper.RunGeneratorWithContainer(source);
        Assert.Contains("IEnumerable<global::TestApp.ICache>", output);
        // Singletons in IEnumerable use concrete type to avoid last-wins issue
        Assert.Contains("GetService(typeof(global::TestApp.Cache))", output);
    }

    [Fact]
    public void IEnumerable_ScopedOnly_ExcludedFromRootResolveKnown()
    {
        var source = """
            using ZInject;
            namespace TestApp;

            public interface IRepo { }

            [Scoped]
            public class Repo : IRepo { }
            """;

        var (output, _) = GeneratorTestHelper.RunGeneratorWithContainer(source);
        // Root ResolveKnown should NOT have IEnumerable<IRepo> (scoped excluded)
        var resolveKnownStart = output.IndexOf("protected override object? ResolveKnown");
        var createScopeStart = output.IndexOf("protected override global::ZInject.Container.ZInjectScope CreateScopeCore");
        var resolveKnown = output.Substring(resolveKnownStart, createScopeStart - resolveKnownStart);
        Assert.DoesNotContain("IEnumerable<global::TestApp.IRepo>", resolveKnown);

        // Scope ResolveScopedKnown SHOULD have it
        var scopeSection = output.Substring(output.IndexOf("ResolveScopedKnown"));
        Assert.Contains("IEnumerable<global::TestApp.IRepo>", scopeSection);
    }

    // --- Standalone provider generation ---

    [Fact]
    public void WhenContainerReferenced_GeneratesStandaloneProvider()
    {
        var source = """
            using ZInject;
            namespace TestApp;
            public interface IFoo { }
            [Transient]
            public class Foo : IFoo { }
            """;
        var (output, _) = GeneratorTestHelper.RunGeneratorWithContainer(source);
        Assert.Contains("TestAssemblyStandaloneServiceProvider", output);
        Assert.Contains("ZInjectStandaloneProvider", output);
    }

    [Fact]
    public void Standalone_HasParameterlessConstructor()
    {
        var source = """
            using ZInject;
            namespace TestApp;
            public interface IFoo { }
            [Transient]
            public class Foo : IFoo { }
            """;
        var (output, _) = GeneratorTestHelper.RunGeneratorWithContainer(source);
        var standaloneSection = output.Substring(output.IndexOf("TestAssemblyStandaloneServiceProvider"));
        Assert.Contains("public TestAssemblyStandaloneServiceProvider() { }", standaloneSection);
    }

    [Fact]
    public void Standalone_ScopeInheritsFromStandaloneScope()
    {
        var source = """
            using ZInject;
            namespace TestApp;
            public interface IFoo { }
            [Transient]
            public class Foo : IFoo { }
            """;
        var (output, _) = GeneratorTestHelper.RunGeneratorWithContainer(source);
        var standaloneSection = output.Substring(output.IndexOf("TestAssemblyStandaloneServiceProvider"));
        Assert.Contains("ZInjectStandaloneScope", standaloneSection);
    }

    [Fact]
    public void Standalone_CreateScopeCore_NoFallbackParameter()
    {
        var source = """
            using ZInject;
            namespace TestApp;
            public interface IFoo { }
            [Transient]
            public class Foo : IFoo { }
            """;
        var (output, _) = GeneratorTestHelper.RunGeneratorWithContainer(source);
        var standaloneSection = output.Substring(output.IndexOf("TestAssemblyStandaloneServiceProvider"));
        Assert.Contains("protected override global::ZInject.Container.ZInjectStandaloneScope CreateScopeCore()", standaloneSection);
        Assert.DoesNotContain("IServiceScope fallbackScope", standaloneSection);
    }

    [Fact]
    public void Standalone_ResolveKnown_SameAsHybrid()
    {
        var source = """
            using ZInject;
            namespace TestApp;
            public interface IFoo { }
            [Transient]
            public class Foo : IFoo { }
            [Singleton]
            public class Bar { }
            """;
        var (output, _) = GeneratorTestHelper.RunGeneratorWithContainer(source);
        var standaloneSection = output.Substring(output.IndexOf("TestAssemblyStandaloneServiceProvider"));
        Assert.Contains("return new global::TestApp.Foo();", standaloneSection);
        Assert.Contains("Interlocked.CompareExchange", standaloneSection);
    }

    [Fact]
    public void Standalone_ScopeConstructor_NoFallbackScope()
    {
        var source = """
            using ZInject;
            namespace TestApp;
            public interface IRepo { }
            [Scoped]
            public class Repo : IRepo { }
            """;
        var (output, _) = GeneratorTestHelper.RunGeneratorWithContainer(source);
        var standaloneSection = output.Substring(output.IndexOf("TestAssemblyStandaloneServiceProvider"));
        Assert.Contains("public Scope(TestAssemblyStandaloneServiceProvider root) : base(root) { }", standaloneSection);
    }

    // --- Standalone singleton disposal ---

    [Fact]
    public void Standalone_DisposableSingleton_GeneratesDisposeOverride()
    {
        var source = """
            using ZInject;
            using System;
            namespace TestApp;
            public interface ICache { }
            [Singleton]
            public class Cache : ICache, IDisposable
            {
                public void Dispose() { }
            }
            """;
        var (output, _) = GeneratorTestHelper.RunGeneratorWithContainer(source);
        var standaloneSection = output.Substring(output.IndexOf("TestAssemblyStandaloneServiceProvider"));
        Assert.Contains("protected override void Dispose(bool disposing)", standaloneSection);
        Assert.Contains("Interlocked.Exchange(ref _singleton_", standaloneSection);
    }

    [Fact]
    public void Standalone_DisposableSingleton_GeneratesDisposeAsyncOverride()
    {
        var source = """
            using ZInject;
            using System;
            namespace TestApp;
            public interface ICache { }
            [Singleton]
            public class Cache : ICache, IDisposable
            {
                public void Dispose() { }
            }
            """;
        var (output, _) = GeneratorTestHelper.RunGeneratorWithContainer(source);
        var standaloneSection = output.Substring(output.IndexOf("TestAssemblyStandaloneServiceProvider"));
        Assert.Contains("override async System.Threading.Tasks.ValueTask DisposeAsync()", standaloneSection);
        Assert.Contains("Interlocked.Exchange(ref _singleton_", standaloneSection);
    }

    [Fact]
    public void Standalone_NonDisposableSingleton_NoDisposeOverride()
    {
        var source = """
            using ZInject;
            namespace TestApp;
            public interface ICache { }
            [Singleton]
            public class Cache : ICache { }
            """;
        var (output, _) = GeneratorTestHelper.RunGeneratorWithContainer(source);
        var standaloneSection = output.Substring(output.IndexOf("TestAssemblyStandaloneServiceProvider"));
        Assert.DoesNotContain("protected override void Dispose(bool disposing)", standaloneSection);
    }

    [Fact]
    public void IEnumerable_MultipleTransients_GeneratesArrayWithAll()
    {
        var source = """
            using ZInject;
            namespace TestApp;

            public interface IHandler { }

            [Transient(AllowMultiple = true)]
            public class HandlerA : IHandler { }

            [Transient(AllowMultiple = true)]
            public class HandlerB : IHandler { }
            """;

        var (output, _) = GeneratorTestHelper.RunGeneratorWithContainer(source);
        Assert.Contains("IEnumerable<global::TestApp.IHandler>", output);
        Assert.Contains("new global::TestApp.HandlerA()", output);
        Assert.Contains("new global::TestApp.HandlerB()", output);
    }

    // --- Task 5: Hybrid container — decorator wrapping ---

    [Fact]
    public void HybridContainer_Decorator_NonGeneric_WrapsInTypeSwitch()
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

        var (output, _) = GeneratorTestHelper.RunGeneratorWithContainer(source);
        // The IFoo case in ResolveKnown should wrap FooImpl in LoggingFoo
        Assert.Contains("new global::LoggingFoo", output);
        Assert.Contains("new global::FooImpl", output);
        // They should appear together (decorator wrapping inner) in the IFoo branch
        // Search within container section (typeof only appears in containers, not extension class)
        var fooIdx = output.IndexOf("typeof(global::IFoo)");
        var loggingIdx = output.IndexOf("new global::LoggingFoo", fooIdx);
        var fooImplIdx = output.IndexOf("new global::FooImpl", fooIdx);
        Assert.True(fooIdx >= 0 && loggingIdx > fooIdx && fooImplIdx > fooIdx);
    }

    // --- Standalone container — open generics (explicit closed-type entries, reflection-free) ---

    [Fact]
    public void Standalone_OpenGeneric_EmitsExplicitClosedTypeEntry()
    {
        // After reflection removal, standalone containers emit explicit if (serviceType == typeof(IRepo<string>))
        // entries rather than runtime delegate caches / MethodInfo lookups.
        var source = """
            using ZInject;
            public interface IRepo<T> { }
            [Scoped]
            public class Repo<T> : IRepo<T> { }
            [Scoped]
            public class Consumer
            {
                public Consumer(IRepo<string> repo) { }
            }
            """;

        var (output, _) = GeneratorTestHelper.RunGeneratorWithContainer(source);
        Assert.DoesNotContain("OG_Factory_0", output);
        Assert.DoesNotContain("_og_dc_0", output);
        Assert.DoesNotContain("MakeGenericType", output);
        Assert.DoesNotContain("GetMethod(", output);
        Assert.Contains("typeof(global::IRepo<string>)", output);
        Assert.Contains("typeof(global::IRepo<>)", output);
        Assert.Contains("typeof(global::Repo<>)", output);
    }

    [Fact]
    public void Standalone_OpenGeneric_ResolveKnown_EmitsExplicitClosedEntry()
    {
        // After reflection removal, standalone containers resolve open generics via explicit
        // typeof(IRepo<string>) == serviceType checks rather than GetGenericTypeDefinition().
        var source = """
            using ZInject;
            public interface IRepo<T> { }
            [Scoped]
            public class Repo<T> : IRepo<T> { }
            [Scoped]
            public class Consumer
            {
                public Consumer(IRepo<string> repo) { }
            }
            """;

        var (output, _) = GeneratorTestHelper.RunGeneratorWithContainer(source);
        Assert.DoesNotContain("GetGenericTypeDefinition()", output);
        Assert.DoesNotContain("ResolveOpenGenericRoot", output);
        Assert.DoesNotContain("ResolveOpenGenericScoped", output);
        Assert.DoesNotContain("OpenGenericMap", output);
        Assert.DoesNotContain("MakeGenericType", output);
        Assert.Contains("typeof(global::IRepo<string>)", output);
    }

    [Fact]
    public void Standalone_Decorator_NonGeneric_WrapsInTypeSwitch()
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

        var (output, _) = GeneratorTestHelper.RunGeneratorWithContainer(source);
        // Standalone type-switch for IFoo wraps FooImpl in LoggingFoo
        var standaloneIdx = output.IndexOf("StandaloneServiceProvider");
        var fooIdx = output.IndexOf("typeof(global::IFoo)", standaloneIdx);
        var loggingIdx = output.IndexOf("new global::LoggingFoo", fooIdx);
        Assert.True(loggingIdx > fooIdx, "Standalone should emit LoggingFoo wrapping FooImpl");
    }

    [Fact]
    public void Standalone_OpenGeneric_WithDecorator_EmitsExplicitClosedEntryWithoutReflection()
    {
        // After reflection removal, standalone containers use explicit closed-type entries.
        // No OG_Factory or _og_dc machinery is emitted.
        var source = """
            using ZInject;
            public interface IRepo<T> { }
            [Scoped]
            public class Repo<T> : IRepo<T> { }
            [Decorator]
            public class LoggingRepo<T> : IRepo<T>
            {
                public LoggingRepo(IRepo<T> inner) { }
            }
            [Scoped]
            public class Consumer
            {
                public Consumer(IRepo<string> repo) { }
            }
            """;

        var (output, _) = GeneratorTestHelper.RunGeneratorWithContainer(source);
        Assert.DoesNotContain("OG_Factory_0", output);
        Assert.DoesNotContain("_og_dc_0", output);
        Assert.DoesNotContain("MakeGenericType", output);
        Assert.DoesNotContain("GetMethod(", output);
        Assert.Contains("typeof(global::IRepo<string>)", output);
        // Decorator must be applied in the standalone container
        Assert.Contains("new global::LoggingRepo<string>(", output);
        Assert.Contains("new global::Repo<string>(", output);
    }

    [Fact]
    public void Standalone_MultiDecorator_EmitsChainingInResolveKnown()
    {
        var source = """
            using ZInject;
            public interface IRepo { }
            [Transient]
            public class ConcreteRepo : IRepo { }
            [Decorator]
            public class CachingRepo : IRepo { public CachingRepo(IRepo inner) { } }
            [Decorator]
            public class LoggingRepo : IRepo { public LoggingRepo(IRepo inner) { } }
            """;

        var (output, _) = GeneratorTestHelper.RunGeneratorWithContainer(source);
        // Standalone provider chains decorators in ResolveKnown
        Assert.Contains("new global::LoggingRepo(", output);
        Assert.Contains("new global::CachingRepo(", output);
        Assert.Contains("new global::ConcreteRepo()", output);
    }

    [Fact]
    public void Hybrid_IsKnownKeyedService_EmitsKeyTypeChecks()
    {
        var source = """
            using ZInject;
            public interface ICache { }
            [Singleton(Key = "memory")]
            public class MemoryCache : ICache { }
            [Singleton(Key = "redis")]
            public class RedisCache : ICache { }
            """;

        var (output, _) = GeneratorTestHelper.RunGeneratorWithContainer(source);
        Assert.Contains("override bool IsKnownKeyedService", output);
        Assert.Contains("serviceKey is string", output);
        Assert.Contains("\"memory\"", output);
        Assert.Contains("\"redis\"", output);
        Assert.Contains("typeof(global::ICache)", output);
    }

    [Fact]
    public void Standalone_IsKnownKeyedService_EmitsKeyTypeChecks()
    {
        var source = """
            using ZInject;
            [assembly: ZInject("Register", standalone: true)]
            public interface ICache { }
            [Singleton(Key = "memory")]
            public class MemoryCache : ICache { }
            """;

        var (output, _) = GeneratorTestHelper.RunGeneratorWithContainer(source);
        Assert.Contains("override bool IsKnownKeyedService", output);
        Assert.Contains("\"memory\"", output);
    }

    [Fact]
    public void IsKnownKeyedService_NoKeyedServices_ReturnsFalse()
    {
        var source = """
            using ZInject;
            public interface IFoo { }
            [Transient]
            public class FooImpl : IFoo { }
            """;

        var (output, _) = GeneratorTestHelper.RunGeneratorWithContainer(source);
        Assert.Contains("override bool IsKnownKeyedService", output);
        Assert.DoesNotContain("serviceKey is string", output);
    }

    [Fact]
    public void Hybrid_IsKnownService_IncludesIsKeyedServiceType()
    {
        var source = """
            using ZInject;
            public interface ICache { }
            [Singleton(Key = "memory")]
            public class MemoryCache : ICache { }
            """;

        var (output, _) = GeneratorTestHelper.RunGeneratorWithContainer(source);
        Assert.Contains("IServiceProviderIsKeyedService", output);
    }

    [Fact]
    public void HybridContainer_DecoratorOf_NonGeneric_WrapsInTypeSwitch()
    {
        var source = """
            using ZInject;
            public interface IFoo { }
            [Transient]
            public class FooImpl : IFoo { }
            [DecoratorOf(typeof(IFoo))]
            public class LoggingFoo : IFoo
            {
                public LoggingFoo(IFoo inner) { }
            }
            """;

        var (output, _) = GeneratorTestHelper.RunGeneratorWithContainer(source);
        Assert.Contains("new global::LoggingFoo", output);
        Assert.Contains("new global::FooImpl", output);
        var fooIdx = output.IndexOf("typeof(global::IFoo)");
        var loggingIdx = output.IndexOf("new global::LoggingFoo", fooIdx);
        Assert.True(fooIdx >= 0 && loggingIdx > fooIdx);
    }

    [Fact]
    public void Standalone_DecoratorOf_NonGeneric_WrapsInTypeSwitch()
    {
        var source = """
            using ZInject;
            public interface IFoo { }
            [Transient]
            public class FooImpl : IFoo { }
            [DecoratorOf(typeof(IFoo))]
            public class LoggingFoo : IFoo
            {
                public LoggingFoo(IFoo inner) { }
            }
            """;

        var (output, _) = GeneratorTestHelper.RunGeneratorWithContainer(source);
        var standaloneIdx = output.IndexOf("StandaloneServiceProvider");
        var fooIdx = output.IndexOf("typeof(global::IFoo)", standaloneIdx);
        var loggingIdx = output.IndexOf("new global::LoggingFoo", fooIdx);
        Assert.True(loggingIdx > fooIdx, "Standalone should emit LoggingFoo wrapping FooImpl");
    }
}
