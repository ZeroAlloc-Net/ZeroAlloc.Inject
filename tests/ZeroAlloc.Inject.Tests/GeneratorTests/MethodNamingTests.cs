namespace ZeroAlloc.Inject.Tests.GeneratorTests;

public class MethodNamingTests
{
    [Fact]
    public void DefaultMethodName_DerivedFromAssemblyName()
    {
        var source = """
            using ZeroAlloc.Inject;
            namespace TestApp;

            [Transient]
            public class Svc { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        // Assembly name in test helper is "TestAssembly"
        Assert.Contains("AddTestAssemblyServices", output);
    }

    [Fact]
    public void ZeroAllocInjectAttribute_OverridesMethodName()
    {
        var source = """
            using ZeroAlloc.Inject;

            [assembly: ZeroAllocInject("AddDomainServices")]

            namespace TestApp;

            [Transient]
            public class Svc { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Contains("AddDomainServices", output);
        Assert.DoesNotContain("AddTestAssemblyServices", output);
    }

    [Fact]
    public void ClassName_DerivedFromMethodName()
    {
        var source = """
            using ZeroAlloc.Inject;

            [assembly: ZeroAllocInject("AddDomainServices")]

            namespace TestApp;

            [Transient]
            public class Svc { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Contains("public static class DomainServicesServiceCollectionExtensions", output);
    }

    [Fact]
    public void MethodNameWithoutAddPrefix_ClassNameHandledCorrectly()
    {
        var source = """
            using ZeroAlloc.Inject;

            [assembly: ZeroAllocInject("RegisterServices")]

            namespace TestApp;

            [Transient]
            public class Svc { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Contains("RegisterServices", output);
        Assert.Contains("RegisterServicesServiceCollectionExtensions", output);
    }
}
