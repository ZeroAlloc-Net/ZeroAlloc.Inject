#nullable enable
using System;

namespace ZeroInject.Generator
{
    internal sealed class ConstructorParameterInfo : IEquatable<ConstructorParameterInfo>
    {
        public string FullyQualifiedTypeName { get; }
        public string ParameterName { get; }
        public bool IsOptional { get; }

        public ConstructorParameterInfo(
            string fullyQualifiedTypeName,
            string parameterName,
            bool isOptional)
        {
            FullyQualifiedTypeName = fullyQualifiedTypeName;
            ParameterName = parameterName;
            IsOptional = isOptional;
        }

        public bool Equals(ConstructorParameterInfo? other)
        {
            if (other is null) return false;
            return FullyQualifiedTypeName == other.FullyQualifiedTypeName
                && ParameterName == other.ParameterName
                && IsOptional == other.IsOptional;
        }

        public override bool Equals(object? obj) => Equals(obj as ConstructorParameterInfo);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + FullyQualifiedTypeName.GetHashCode();
                hash = hash * 31 + ParameterName.GetHashCode();
                return hash;
            }
        }
    }
}
