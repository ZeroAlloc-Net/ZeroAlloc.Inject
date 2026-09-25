#nullable enable
using System;

namespace ZeroAlloc.Inject.Generator
{
    /// <summary>
    /// The resolved value of the org-wide "ZeroAllocGeneratedAccessibility" MSBuild property:
    /// the C# accessibility keyword ("public" or "internal") the generator should emit for every
    /// public entry point it produces, plus the raw value when it was invalid (so ZAI020 can be
    /// reported once, with the offending value, from the pipeline's output step).
    /// </summary>
    internal readonly struct GeneratedAccessibilityOption : IEquatable<GeneratedAccessibilityOption>
    {
        public string Keyword { get; }
        public string? InvalidValue { get; }

        public GeneratedAccessibilityOption(string keyword, string? invalidValue)
        {
            Keyword = keyword;
            InvalidValue = invalidValue;
        }

        public bool Equals(GeneratedAccessibilityOption other) =>
            Keyword == other.Keyword && InvalidValue == other.InvalidValue;

        public override bool Equals(object? obj) =>
            obj is GeneratedAccessibilityOption other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + Keyword.GetHashCode();
                hash = hash * 31 + (InvalidValue?.GetHashCode() ?? 0);
                return hash;
            }
        }
    }
}
