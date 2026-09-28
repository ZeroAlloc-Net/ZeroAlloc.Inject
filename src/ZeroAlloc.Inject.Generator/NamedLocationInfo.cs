#nullable enable
using System;

namespace ZeroAlloc.Inject.Generator
{
    /// <summary>A named member, such as a property, and where it is declared.</summary>
    internal sealed class NamedLocationInfo : IEquatable<NamedLocationInfo>
    {
        public string Name { get; }
        public LocationInfo? Location { get; }

        public NamedLocationInfo(string name, LocationInfo? location)
        {
            Name = name;
            Location = location;
        }

        public bool Equals(NamedLocationInfo? other) =>
            other is not null
            && string.Equals(Name, other.Name, StringComparison.Ordinal)
            && Equals(Location, other.Location);

        public override bool Equals(object? obj) => Equals(obj as NamedLocationInfo);

        public override int GetHashCode()
        {
            unchecked
            {
                return StringComparer.Ordinal.GetHashCode(Name) * 31 + (Location?.GetHashCode() ?? 0);
            }
        }
    }
}
