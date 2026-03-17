#nullable enable
using System;

namespace ZeroAlloc.Inject.Generator
{
    internal sealed class PropertyInjectionInfo : IEquatable<PropertyInjectionInfo>
    {
        public string FullyQualifiedTypeName { get; }
        public string PropertyName { get; }
        public bool IsRequired { get; }

        public PropertyInjectionInfo(string fullyQualifiedTypeName, string propertyName, bool isRequired)
        {
            FullyQualifiedTypeName = fullyQualifiedTypeName;
            PropertyName = propertyName;
            IsRequired = isRequired;
        }

        public bool Equals(PropertyInjectionInfo? other)
        {
            if (other is null) return false;
            return FullyQualifiedTypeName == other.FullyQualifiedTypeName
                && PropertyName == other.PropertyName
                && IsRequired == other.IsRequired;
        }

        public override bool Equals(object? obj) => Equals(obj as PropertyInjectionInfo);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + FullyQualifiedTypeName.GetHashCode();
                hash = hash * 31 + PropertyName.GetHashCode();
                hash = hash * 31 + IsRequired.GetHashCode();
                return hash;
            }
        }
    }
}
