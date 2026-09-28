#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Inject.Generator
{
    /// <summary>
    /// A diagnostic as the incremental pipeline caches it: the descriptor, the message arguments and
    /// the <see cref="LocationInfo"/> values, with value equality. A null location means the diagnostic
    /// is not about any source element, such as a missing reference or an MSBuild property.
    /// </summary>
    internal sealed class DiagnosticInfo : IEquatable<DiagnosticInfo>
    {
        public DiagnosticDescriptor Descriptor { get; }
        public LocationInfo? Location { get; }
        public ImmutableArray<LocationInfo> AdditionalLocations { get; }
        public ImmutableArray<string?> MessageArgs { get; }

        public DiagnosticInfo(
            DiagnosticDescriptor descriptor,
            LocationInfo? location,
            ImmutableArray<LocationInfo> additionalLocations,
            params string?[] messageArgs)
        {
            Descriptor = descriptor;
            Location = location;
            AdditionalLocations = additionalLocations.IsDefault ? ImmutableArray<LocationInfo>.Empty : additionalLocations;
            MessageArgs = messageArgs.ToImmutableArray();
        }

        public DiagnosticInfo(DiagnosticDescriptor descriptor, LocationInfo? location, params string?[] messageArgs)
            : this(descriptor, location, ImmutableArray<LocationInfo>.Empty, messageArgs)
        {
        }

        public Diagnostic ToDiagnostic(SyntaxTreeLookup trees)
        {
            IEnumerable<Location>? additional = null;
            if (!AdditionalLocations.IsEmpty)
            {
                var builder = new List<Location>(AdditionalLocations.Length);
                foreach (var info in AdditionalLocations)
                {
                    builder.Add(info.ToLocation(trees));
                }
                additional = builder;
            }

            var args = new object?[MessageArgs.Length];
            for (int i = 0; i < args.Length; i++)
            {
                args[i] = MessageArgs[i];
            }

            return Diagnostic.Create(
                Descriptor,
                Location?.ToLocation(trees) ?? Microsoft.CodeAnalysis.Location.None,
                additional,
                args);
        }

        public bool Equals(DiagnosticInfo? other) =>
            other is not null
            && Descriptor.Equals(other.Descriptor)
            && Equals(Location, other.Location)
            && AdditionalLocations.SequenceEqual(other.AdditionalLocations)
            && MessageArgs.SequenceEqual(other.MessageArgs, StringComparer.Ordinal);

        public override bool Equals(object? obj) => Equals(obj as DiagnosticInfo);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + StringComparer.Ordinal.GetHashCode(Descriptor.Id);
                hash = hash * 31 + (Location?.GetHashCode() ?? 0);
                hash = hash * 31 + MessageArgs.Length;
                return hash;
            }
        }
    }

    /// <summary>
    /// Compares two arrays element by element. <see cref="ImmutableArray{T}"/> itself compares by
    /// reference, so a step that returns a new array with the same contents would look modified.
    /// </summary>
    internal sealed class SequenceComparer<T> : IEqualityComparer<ImmutableArray<T>>
    {
        private readonly IEqualityComparer<T> _elementComparer;

        public SequenceComparer(IEqualityComparer<T>? elementComparer = null)
        {
            _elementComparer = elementComparer ?? EqualityComparer<T>.Default;
        }

        public static SequenceComparer<T> Instance { get; } = new SequenceComparer<T>();

        public bool Equals(ImmutableArray<T> x, ImmutableArray<T> y)
        {
            if (x.IsDefault || y.IsDefault)
            {
                return x.IsDefault && y.IsDefault;
            }

            return x.SequenceEqual(y, _elementComparer);
        }

        public int GetHashCode(ImmutableArray<T> obj)
        {
            if (obj.IsDefault)
            {
                return 0;
            }

            unchecked
            {
                var hash = 17;
                foreach (var item in obj)
                {
                    hash = hash * 31 + (item == null ? 0 : _elementComparer.GetHashCode(item));
                }
                return hash;
            }
        }
    }

    /// <summary>
    /// Compares reported diagnostics by what a user sees. <see cref="Diagnostic.Equals(Diagnostic)"/>
    /// leaves out the additional locations and compares message arguments by reference, so it is not
    /// used: a moved cycle member would keep its old place in a cached report.
    /// </summary>
    internal sealed class ReportedDiagnosticComparer : IEqualityComparer<Diagnostic>
    {
        public static ReportedDiagnosticComparer Instance { get; } = new ReportedDiagnosticComparer();

        public bool Equals(Diagnostic? x, Diagnostic? y)
        {
            if (x is null || y is null)
            {
                return x is null && y is null;
            }

            return x.Descriptor.Equals(y.Descriptor)
                && x.Location.Equals(y.Location)
                && x.AdditionalLocations.SequenceEqual(y.AdditionalLocations)
                && string.Equals(
                    x.GetMessage(System.Globalization.CultureInfo.InvariantCulture),
                    y.GetMessage(System.Globalization.CultureInfo.InvariantCulture),
                    StringComparison.Ordinal);
        }

        public int GetHashCode(Diagnostic obj) =>
            unchecked(StringComparer.Ordinal.GetHashCode(obj.Id) * 31 + obj.Location.GetHashCode());
    }
}
