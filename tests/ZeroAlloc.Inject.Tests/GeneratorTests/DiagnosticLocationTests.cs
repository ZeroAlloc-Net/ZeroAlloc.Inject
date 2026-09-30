using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace ZeroAlloc.Inject.Tests.GeneratorTests;

/// <summary>
/// Every ZAI diagnostic that belongs to a class, attribute or member is reported at that source
/// location, bound to the syntax tree, so the IDE can point at it and #pragma can suppress it.
/// A source marked with [| and |] gives the expected spans; the markers are removed before it runs.
/// </summary>
public class DiagnosticLocationTests
{
    [Theory]
    // ZAI001: the second lifetime attribute, the one that makes the set invalid.
    [InlineData("ZAI001", """
        using ZeroAlloc.Inject;
        public interface IFoo { }
        [Transient]
        [[|Singleton|]]
        public class Dual : IFoo { }
        """)]
    // ZAI003: the class identifier.
    [InlineData("ZAI003", """
        using ZeroAlloc.Inject;
        public interface IFoo { }
        [Transient]
        public abstract class [|AbstractService|] : IFoo { }
        """)]
    // ZAI004: the attribute that carries the As property.
    [InlineData("ZAI004", """
        using ZeroAlloc.Inject;
        public interface IFoo { }
        public interface IBar { }
        [[|Transient(As = typeof(IBar))|]]
        public class Foo : IFoo { }
        """)]
    // ZAI006: the class identifier.
    [InlineData("ZAI006", """
        using ZeroAlloc.Inject;
        public interface IFoo { }
        [Transient]
        public class [|NoCtorService|] : IFoo
        {
            private NoCtorService() { }
        }
        """)]
    // ZAI007: the class identifier.
    [InlineData("ZAI007", """
        using ZeroAlloc.Inject;
        [Transient]
        public class [|PlainService|] { }
        """)]
    // ZAI009: the class identifier.
    [InlineData("ZAI009", """
        using ZeroAlloc.Inject;
        public interface IFoo { }
        public interface IBar { }
        [Transient]
        public class [|TwoCtors|] : IFoo
        {
            public TwoCtors() { }
            public TwoCtors(IBar bar) { }
        }
        """)]
    // ZAI010: the offending constructor parameter.
    [InlineData("ZAI010", """
        using ZeroAlloc.Inject;
        public interface IFoo { }
        [Transient]
        public class Named : IFoo
        {
            public Named(string [|name|]) { }
        }
        """)]
    // ZAI011: the decorator class identifier.
    [InlineData("ZAI011", """
        using ZeroAlloc.Inject;
        public interface IFoo { }
        public interface IBar { }
        [Decorator]
        public class [|LoggingFoo|] : IFoo
        {
            public LoggingFoo(IBar unrelated) { }
        }
        [Transient]
        public class FooImpl : IFoo { }
        """)]
    // ZAI012: the [Decorator] attribute.
    [InlineData("ZAI012", """
        using ZeroAlloc.Inject;
        public interface IFoo { }
        [[|Decorator|]]
        public class LoggingFoo : IFoo
        {
            public LoggingFoo(IFoo inner) { }
        }
        """)]
    // ZAI013: the decorator class identifier.
    [InlineData("ZAI013", """
        using ZeroAlloc.Inject;
        public interface IFoo { }
        [Decorator]
        public abstract class [|LoggingFoo|] : IFoo
        {
            public LoggingFoo(IFoo inner) { }
        }
        """)]
    // ZAI015: the offending constructor parameter.
    [InlineData("ZAI015", """
        #nullable enable
        using ZeroAlloc.Inject;
        public interface ILogger { }
        public interface IFoo { }
        [Transient]
        public class FooImpl : IFoo
        {
            public FooImpl([OptionalDependency] ILogger [|logger|]) { }
        }
        """)]
    // ZAI016: the [DecoratorOf] attribute that names the interface.
    [InlineData("ZAI016", """
        using ZeroAlloc.Inject;
        public interface IFoo { }
        public interface IBar { }
        [Transient]
        public class FooImpl : IFoo { }
        [[|DecoratorOf(typeof(IBar))|]]
        public class BadDecorator : IFoo
        {
            public BadDecorator(IFoo inner) { }
        }
        """)]
    // ZAI018: the open generic class identifier.
    [InlineData("ZAI018", """
        using ZeroAlloc.Inject;
        public interface IRepository<T> { }
        [Transient]
        public class [|Repository|]<T> : IRepository<T> { }
        """)]
    // ZAI019: the non-settable property.
    [InlineData("ZAI019", """
        using ZeroAlloc.Inject;
        public interface IDep { }
        public interface IFoo { }
        [Transient]
        public class WithProp : IFoo
        {
            [Inject]
            public IDep [|Dep|] { get; } = null!;
        }
        """)]
    public void Diagnostic_IsReportedAtItsSourceLocation(string id, string markedSource)
    {
        var (source, spans) = Unmark(markedSource);

        var (_, diagnostics) = GeneratorTestHelper.RunGeneratorOnFile(source);

        var diagnostic = AssertEx.One(diagnostics, d => string.Equals(d.Id, id, StringComparison.Ordinal));
        AssertAt(diagnostic.Location, source, AssertEx.One(spans));
    }

    [Fact]
    public void ZAI014_IsReportedAtTheFirstClassOfTheCycle_WithTheOthersAsAdditionalLocations()
    {
        var (source, spans) = Unmark("""
            using ZeroAlloc.Inject;
            public interface IA { }
            public interface IB { }
            [Transient]
            public class [|A|] : IA { public A(IB b) { } }
            [Transient]
            public class [|B|] : IB { public B(IA a) { } }
            """);

        var (_, diagnostics) = GeneratorTestHelper.RunGeneratorOnFile(source);

        var diagnostic = AssertEx.One(diagnostics, d => string.Equals(d.Id, "ZAI014", StringComparison.Ordinal));
        AssertAt(diagnostic.Location, source, spans[0]);
        AssertAt(AssertEx.One(diagnostic.AdditionalLocations), source, spans[1]);
    }

    [Fact]
    public void ZAI017_IsReportedAtTheLaterAttribute_WithTheEarlierAsAdditionalLocation()
    {
        var (source, spans) = Unmark("""
            using ZeroAlloc.Inject;
            public interface IFoo { }
            [Transient]
            public class FooImpl : IFoo { }
            [[|DecoratorOf(typeof(IFoo), Order = 1)|]]
            public class DecoratorA : IFoo
            {
                public DecoratorA(IFoo inner) { }
            }
            [[|DecoratorOf(typeof(IFoo), Order = 1)|]]
            public class DecoratorB : IFoo
            {
                public DecoratorB(IFoo inner) { }
            }
            """);

        var (_, diagnostics) = GeneratorTestHelper.RunGeneratorOnFile(source);

        var diagnostic = AssertEx.One(diagnostics, d => string.Equals(d.Id, "ZAI017", StringComparison.Ordinal));
        AssertAt(diagnostic.Location, source, spans[1]);
        AssertAt(AssertEx.One(diagnostic.AdditionalLocations), source, spans[0]);
    }

    [Fact]
    public void ZAI021_IsReportedAtTheLaterClass_WithTheEarlierAsAdditionalLocation()
    {
        var (source, spans) = Unmark("""
            using ZeroAlloc.Inject;
            public interface IFoo { }
            [Transient]
            public class [|First|] : IFoo { }
            [Transient]
            public class [|Second|] : IFoo { }
            """);

        var (_, diagnostics) = GeneratorTestHelper.RunGeneratorOnFile(source);

        var diagnostic = AssertEx.One(diagnostics, d => string.Equals(d.Id, "ZAI021", StringComparison.Ordinal));
        AssertAt(diagnostic.Location, source, spans[1]);
        AssertAt(AssertEx.One(diagnostic.AdditionalLocations), source, spans[0]);
    }

    [Fact]
    public void ZAI001_OnAClassWithThreeLifetimes_IsReportedOnceAtTheSecondAttribute()
    {
        var (source, spans) = Unmark("""
            using ZeroAlloc.Inject;
            public interface IFoo { }
            [Singleton]
            [[|Transient|]]
            [Scoped]
            public class Triple : IFoo { }
            """);

        var (_, diagnostics) = GeneratorTestHelper.RunGeneratorOnFile(source);

        var diagnostic = AssertEx.One(diagnostics, d => string.Equals(d.Id, "ZAI001", StringComparison.Ordinal));
        AssertAt(diagnostic.Location, source, AssertEx.One(spans));
    }

    [Fact]
    public void ZAI008_IsProjectLevel_AndHasNoSourceLocation()
    {
        var (_, diagnostics) = GeneratorTestHelper.RunGeneratorOnFile(
            """
            using ZeroAlloc.Inject;
            public interface IFoo { }
            [Transient]
            public class Foo : IFoo { }
            """,
            includeDependencyInjection: false);

        var diagnostic = AssertEx.One(diagnostics, d => string.Equals(d.Id, "ZAI008", StringComparison.Ordinal));
        Assert.Equal(Location.None, diagnostic.Location);
    }

    [Fact]
    public void ZAI020_IsAnMSBuildPropertyError_AndHasNoSourceLocation()
    {
        var (_, diagnostics) = GeneratorTestHelper.RunGeneratorOnFile(
            """
            using ZeroAlloc.Inject;
            public interface IFoo { }
            [Transient]
            public class Foo : IFoo { }
            """,
            accessibilityValue: "Protected");

        var diagnostic = AssertEx.One(diagnostics, d => string.Equals(d.Id, "ZAI020", StringComparison.Ordinal));
        Assert.Equal(Location.None, diagnostic.Location);
    }

    [Fact]
    public void PragmaAroundOneClass_SuppressesThatDiagnosticOnly()
    {
        // Quiet reports ZAI006 and ZAI007; the pragma names only ZAI007. Loud reports ZAI007 outside it.
        var source = """
            using ZeroAlloc.Inject;

            #pragma warning disable ZAI007
            [Transient]
            public class Quiet
            {
                private Quiet() { }
            }
            #pragma warning restore ZAI007

            [Transient]
            public class Loud { }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGeneratorOnFile(source);

        var zai007 = diagnostics.Where(d => string.Equals(d.Id, "ZAI007", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, zai007.Count);
        Assert.True(Single(zai007, "Quiet").IsSuppressed);
        Assert.False(Single(zai007, "Loud").IsSuppressed);

        var zai006 = AssertEx.One(diagnostics, d => string.Equals(d.Id, "ZAI006", StringComparison.Ordinal));
        Assert.False(zai006.IsSuppressed);

        static Diagnostic Single(List<Diagnostic> list, string className) =>
            AssertEx.One(list, d => d.GetMessage(System.Globalization.CultureInfo.InvariantCulture)
                .Contains("'" + className + "'", StringComparison.Ordinal));
    }

    internal static void AssertAt(Location location, string source, TextSpan expected)
    {
        // A source location, bound to the tree, is what #pragma and the IDE need.
        Assert.Equal(LocationKind.SourceFile, location.Kind);
        Assert.Equal(GeneratorTestHelper.TestFilePath, location.SourceTree!.FilePath);
        Assert.Equal(expected, location.SourceSpan);

        var lineSpan = location.GetLineSpan();
        Assert.Equal(GeneratorTestHelper.TestFilePath, lineSpan.Path);
        Assert.Equal(SourceText.From(source).Lines.GetLinePositionSpan(expected), lineSpan.Span);
    }

    internal static (string source, List<TextSpan> spans) Unmark(string marked)
    {
        var sb = new StringBuilder(marked.Length);
        var spans = new List<TextSpan>();
        int start = -1;
        for (int i = 0; i < marked.Length; i++)
        {
            if (string.CompareOrdinal(marked, i, "[|", 0, 2) == 0)
            {
                start = sb.Length;
                i++;
            }
            else if (string.CompareOrdinal(marked, i, "|]", 0, 2) == 0)
            {
                spans.Add(TextSpan.FromBounds(start, sb.Length));
                i++;
            }
            else
            {
                sb.Append(marked[i]);
            }
        }

        return (sb.ToString(), spans);
    }
}
