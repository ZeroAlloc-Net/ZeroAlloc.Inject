using System;
using System.Collections.Generic;
using System.Linq;

namespace ZeroAlloc.Inject.Tests.GeneratorTests;

/// <summary>Asserts exactly one match and returns it, without LINQ Single, which HLQ005 flags.</summary>
internal static class AssertEx
{
    public static T One<T>(IEnumerable<T> items) => One(items, static _ => true);

    public static T One<T>(IEnumerable<T> items, Func<T, bool> predicate)
    {
        var matches = items.Where(predicate).ToList();
        Assert.True(matches.Count == 1, $"Expected exactly one match, found {matches.Count}.");
        return matches[0];
    }
}
