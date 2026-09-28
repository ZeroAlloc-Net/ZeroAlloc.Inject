#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace ZeroAlloc.Inject.Generator
{
    /// <summary>
    /// A source location that the incremental pipeline can cache. A <see cref="Location"/> holds its
    /// <see cref="SyntaxTree"/>, and a new tree on every edit would make every cached model unequal.
    /// This record holds only values, and <see cref="ToLocation"/> binds it back to the compilation's
    /// tree when the diagnostic is reported.
    /// </summary>
    internal sealed class LocationInfo : IEquatable<LocationInfo>
    {
        public string FilePath { get; }
        public TextSpan Span { get; }
        public LinePositionSpan LineSpan { get; }

        public LocationInfo(string filePath, TextSpan span, LinePositionSpan lineSpan)
        {
            FilePath = filePath;
            Span = span;
            LineSpan = lineSpan;
        }

        public static LocationInfo? From(Location? location)
        {
            if (location?.SourceTree is not { } tree)
            {
                return null;
            }

            return new LocationInfo(tree.FilePath, location.SourceSpan, location.GetLineSpan().Span);
        }

        public static LocationInfo? From(SyntaxReference? reference)
        {
            if (reference == null)
            {
                return null;
            }

            var tree = reference.SyntaxTree;
            return new LocationInfo(tree.FilePath, reference.Span, tree.GetLineSpan(reference.Span).Span);
        }

        /// <summary>
        /// Rebuilds the location. When the file is a tree of the compilation, the result is a source
        /// location in that tree, which #pragma warning disable can suppress. A location created from
        /// the path alone is an external file location, which #pragma cannot reach.
        /// </summary>
        public Location ToLocation(SyntaxTreeLookup trees)
        {
            var tree = trees.Find(FilePath);
            return tree != null && Span.End <= tree.Length
                ? Location.Create(tree, Span)
                : Location.Create(FilePath, Span, LineSpan);
        }

        public bool Equals(LocationInfo? other) =>
            other is not null
            && string.Equals(FilePath, other.FilePath, StringComparison.Ordinal)
            && Span.Equals(other.Span)
            && LineSpan.Equals(other.LineSpan);

        public override bool Equals(object? obj) => Equals(obj as LocationInfo);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + StringComparer.Ordinal.GetHashCode(FilePath);
                hash = hash * 31 + Span.GetHashCode();
                hash = hash * 31 + LineSpan.GetHashCode();
                return hash;
            }
        }
    }

    /// <summary>
    /// Finds a compilation's syntax tree by file path. The lookup is built on first use, so a run
    /// without diagnostics never walks the trees. A path shared by two trees is ambiguous, and
    /// neither tree is returned for it.
    /// </summary>
    internal sealed class SyntaxTreeLookup
    {
        private readonly Compilation _compilation;
        private Dictionary<string, SyntaxTree?>? _byPath;

        public SyntaxTreeLookup(Compilation compilation)
        {
            _compilation = compilation;
        }

        public SyntaxTree? Find(string filePath)
        {
            if (_byPath == null)
            {
                _byPath = new Dictionary<string, SyntaxTree?>(StringComparer.Ordinal);
                foreach (var tree in _compilation.SyntaxTrees)
                {
                    _byPath[tree.FilePath] = _byPath.ContainsKey(tree.FilePath) ? null : tree;
                }
            }

            return _byPath.TryGetValue(filePath, out var found) ? found : null;
        }
    }
}
