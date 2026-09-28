#nullable enable
using System;
using System.Collections.Immutable;
using System.Linq;

namespace ZeroAlloc.Inject.Generator
{
    internal sealed class ConstructorParameterInfo : IEquatable<ConstructorParameterInfo>
    {
        public string FullyQualifiedTypeName { get; }
        public string ParameterName { get; }
        public bool IsOptional { get; }
        public string? UnboundGenericInterfaceFqn { get; }
        /// <summary>
        /// The type arguments of a closed generic parameter type, as documentation-comment reference
        /// IDs. A reference ID round-trips every type argument through
        /// <c>DocumentationCommentId.GetFirstSymbolForReferenceId</c>, including a constructed generic
        /// such as <c>int?</c>, a nested type and an array, while staying a plain string the
        /// incremental pipeline can compare.
        /// </summary>
        public ImmutableArray<string> TypeArgumentReferenceIds { get; }

        public ConstructorParameterInfo(
            string fullyQualifiedTypeName,
            string parameterName,
            bool isOptional,
            string? unboundGenericInterfaceFqn = null,
            ImmutableArray<string> typeArgumentReferenceIds = default)
        {
            FullyQualifiedTypeName = fullyQualifiedTypeName;
            ParameterName = parameterName;
            IsOptional = isOptional;
            UnboundGenericInterfaceFqn = unboundGenericInterfaceFqn;
            TypeArgumentReferenceIds = typeArgumentReferenceIds.IsDefault
                ? ImmutableArray<string>.Empty
                : typeArgumentReferenceIds;
        }

        public bool Equals(ConstructorParameterInfo? other)
        {
            if (other is null) return false;
            return FullyQualifiedTypeName == other.FullyQualifiedTypeName
                && ParameterName == other.ParameterName
                && IsOptional == other.IsOptional
                && UnboundGenericInterfaceFqn == other.UnboundGenericInterfaceFqn
                && TypeArgumentReferenceIds.SequenceEqual(other.TypeArgumentReferenceIds);
        }

        public override bool Equals(object? obj) => Equals(obj as ConstructorParameterInfo);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + FullyQualifiedTypeName.GetHashCode();
                hash = hash * 31 + ParameterName.GetHashCode();
                hash = hash * 31 + IsOptional.GetHashCode();
                hash = hash * 31 + (UnboundGenericInterfaceFqn?.GetHashCode() ?? 0);
                foreach (var name in TypeArgumentReferenceIds)
                    hash = hash * 31 + name.GetHashCode();
                return hash;
            }
        }
    }
}
