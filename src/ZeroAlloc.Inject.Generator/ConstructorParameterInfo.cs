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
        public ImmutableArray<string> TypeArgumentMetadataNames { get; }

        public ConstructorParameterInfo(
            string fullyQualifiedTypeName,
            string parameterName,
            bool isOptional,
            string? unboundGenericInterfaceFqn = null,
            ImmutableArray<string> typeArgumentMetadataNames = default)
        {
            FullyQualifiedTypeName = fullyQualifiedTypeName;
            ParameterName = parameterName;
            IsOptional = isOptional;
            UnboundGenericInterfaceFqn = unboundGenericInterfaceFqn;
            TypeArgumentMetadataNames = typeArgumentMetadataNames.IsDefault
                ? ImmutableArray<string>.Empty
                : typeArgumentMetadataNames;
        }

        public bool Equals(ConstructorParameterInfo? other)
        {
            if (other is null) return false;
            return FullyQualifiedTypeName == other.FullyQualifiedTypeName
                && ParameterName == other.ParameterName
                && IsOptional == other.IsOptional
                && UnboundGenericInterfaceFqn == other.UnboundGenericInterfaceFqn
                && TypeArgumentMetadataNames.SequenceEqual(other.TypeArgumentMetadataNames);
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
                foreach (var name in TypeArgumentMetadataNames)
                    hash = hash * 31 + name.GetHashCode();
                return hash;
            }
        }
    }
}
