namespace ZeroInject.Tests.GeneratorTests;

public class MethodNamingTests
{
    [Fact]
    public void DefaultMethodName_DerivedFromAssemblyName()
    {
        var source = """
            using ZeroInject;
            namespace TestApp;

            [Transient]
            public class Svc { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        // Assembly name in test helper is "TestAssembly"
        Assert.Contains("AddTestAssemblyServices", output);
    }

    [Fact]
    public void ZeroInjectAttribute_OverridesMethodName()
    {
        var source = """
            using ZeroInject;

            [assembly: ZeroInject("AddDomainServices")]

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
            using ZeroInject;

            [assembly: ZeroInject("AddDomainServices")]

            namespace TestApp;

            [Transient]
            public class Svc { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Contains("public static class DomainServicesServiceCollectionExtensions", output);
    }
}
