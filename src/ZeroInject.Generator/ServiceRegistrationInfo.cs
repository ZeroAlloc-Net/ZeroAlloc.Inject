#nullable enable
using System;
using System.Collections.Generic;

namespace ZeroInject.Generator
{
    internal sealed class ServiceRegistrationInfo : IEquatable<ServiceRegistrationInfo>
    {
        public string Namespace { get; }
        public string TypeName { get; }
        public string FullyQualifiedName { get; }
        public string Lifetime { get; }
        public List<string> Interfaces { get; }
        public string? AsType { get; }
        public string? Key { get; }
        public bool AllowMultiple { get; }
        public bool IsOpenGeneric { get; }
        public string? OpenGenericArity { get; }
        public bool HasPublicConstructor { get; }
        public List<ConstructorParameterInfo> ConstructorParameters { get; }
        public bool HasMultipleConstructors { get; }
        public string? PrimitiveParameterName { get; }
        public string? PrimitiveParameterType { get; }

        public ServiceRegistrationInfo(
            string ns,
            string typeName,
            string fullyQualifiedName,
            string lifetime,
            List<string> interfaces,
            string? asType,
            string? key,
            bool allowMultiple,
            bool isOpenGeneric,
            string? openGenericArity,
            bool hasPublicConstructor,
            List<ConstructorParameterInfo> constructorParameters,
            bool hasMultipleConstructors,
            string? primitiveParameterName,
            string? primitiveParameterType)
        {
            Namespace = ns;
            TypeName = typeName;
            FullyQualifiedName = fullyQualifiedName;
            Lifetime = lifetime;
            Interfaces = interfaces;
            AsType = asType;
            Key = key;
            AllowMultiple = allowMultiple;
            IsOpenGeneric = isOpenGeneric;
            OpenGenericArity = openGenericArity;
            HasPublicConstructor = hasPublicConstructor;
            ConstructorParameters = constructorParameters;
            HasMultipleConstructors = hasMultipleConstructors;
            PrimitiveParameterName = primitiveParameterName;
            PrimitiveParameterType = primitiveParameterType;
        }

        public bool Equals(ServiceRegistrationInfo? other)
        {
            if (other is null) return false;
            return FullyQualifiedName == other.FullyQualifiedName
                && Lifetime == other.Lifetime
                && AsType == other.AsType
                && Key == other.Key
                && AllowMultiple == other.AllowMultiple
                && ConstructorParameters.Count == other.ConstructorParameters.Count;
        }

        public override bool Equals(object? obj) => Equals(obj as ServiceRegistrationInfo);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + FullyQualifiedName.GetHashCode();
                hash = hash * 31 + Lifetime.GetHashCode();
                return hash;
            }
        }
    }
}
