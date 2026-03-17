using System;
using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Inject.Tests.GeneratorTests;

public class PropertyInjectionGeneratorTests
{
    [Fact]
    public void InjectProperty_Required_GeneratesBlockLambdaWithGetRequiredService()
    {
        var source = """
            using ZeroAlloc.Inject;
            namespace TestApp;

            public interface IDep { }
            public interface IMyService { }

            [Transient]
            public class MyService : IMyService
            {
                [Inject]
                public IDep Dep { get; set; } = null!;
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("var instance = new global::TestApp.MyService()", output);
        Assert.Contains("instance.Dep = sp.GetRequiredService<global::TestApp.IDep>()", output);
        Assert.Contains("return instance;", output);
    }

    [Fact]
    public void InjectProperty_RequiredFalse_GeneratesBlockLambdaWithGetService()
    {
        var source = """
            using ZeroAlloc.Inject;
            namespace TestApp;

            public interface IOptDep { }
            public interface IMyService { }

            [Transient]
            public class MyService : IMyService
            {
                [Inject(Required = false)]
                public IOptDep? OptDep { get; set; }
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("instance.OptDep = sp.GetService<global::TestApp.IOptDep>()", output);
    }

    [Fact]
    public void InjectProperty_NoSetter_ProducesZAI019()
    {
        var source = """
            using ZeroAlloc.Inject;
            namespace TestApp;

            public interface IDep { }
            public interface IMyService { }

            [Transient]
            public class MyService : IMyService
            {
                [Inject]
                public IDep Dep { get; } = null!;
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Contains(diagnostics, d => string.Equals(d.Id, "ZAI019", StringComparison.Ordinal));
    }

    [Fact]
    public void InjectProperty_WithConstructorDeps_GeneratesBlockLambdaWithBoth()
    {
        var source = """
            using ZeroAlloc.Inject;
            namespace TestApp;

            public interface ICtorDep { }
            public interface IPropDep { }
            public interface IMyService { }

            [Transient]
            public class MyService : IMyService
            {
                public MyService(ICtorDep ctorDep) { }

                [Inject]
                public IPropDep PropDep { get; set; } = null!;
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("sp.GetRequiredService<global::TestApp.ICtorDep>()", output);
        Assert.Contains("instance.PropDep = sp.GetRequiredService<global::TestApp.IPropDep>()", output);
    }

    [Fact]
    public void NoInjectProperty_StillGeneratesExpressionLambda()
    {
        var source = """
            using ZeroAlloc.Inject;
            namespace TestApp;

            public interface IMyService { }

            [Transient]
            public class MyService : IMyService { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("sp => new global::TestApp.MyService()", output);
        Assert.DoesNotContain("var instance =", output);
    }

    [Fact]
    public void InjectProperty_InitOnlySetter_ProducesZAI019()
    {
        var source = """
            using ZeroAlloc.Inject;
            namespace TestApp;

            public interface IDep { }
            public interface IMyService { }

            [Transient]
            public class MyService : IMyService
            {
                [Inject]
                public IDep Dep { get; init; } = null!;
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Contains(diagnostics, d => string.Equals(d.Id, "ZAI019", StringComparison.Ordinal));
    }
}
