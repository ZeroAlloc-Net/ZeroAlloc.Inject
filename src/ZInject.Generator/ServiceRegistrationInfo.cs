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
        public bool ImplementsDisposable { get; }

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
            string? primitiveParameterType,
            bool implementsDisposable)
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
            ImplementsDisposable = implementsDisposable;
        }

        public bool Equals(ServiceRegistrationInfo? other)
        {
            if (other is null) return false;
            if (FullyQualifiedName != other.FullyQualifiedName
                || Lifetime != other.Lifetime
                || AsType != other.AsType
                || Key != other.Key
                || AllowMultiple != other.AllowMultiple
                || IsOpenGeneric != other.IsOpenGeneric
                || HasPublicConstructor != other.HasPublicConstructor
                || HasMultipleConstructors != other.HasMultipleConstructors
                || PrimitiveParameterName != other.PrimitiveParameterName
                || PrimitiveParameterType != other.PrimitiveParameterType
                || ImplementsDisposable != other.ImplementsDisposable
                || ConstructorParameters.Count != other.ConstructorParameters.Count
                || Interfaces.Count != other.Interfaces.Count)
            {
                return false;
            }

            for (int i = 0; i < Interfaces.Count; i++)
            {
                if (Interfaces[i] != other.Interfaces[i])
                {
                    return false;
                }
            }

            for (int i = 0; i < ConstructorParameters.Count; i++)
            {
                if (!ConstructorParameters[i].Equals(other.ConstructorParameters[i]))
                {
                    return false;
                }
            }

            return true;
        }

        public override bool Equals(object? obj) => Equals(obj as ServiceRegistrationInfo);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + FullyQualifiedName.GetHashCode();
                hash = hash * 31 + Lifetime.GetHashCode();
                hash = hash * 31 + IsOpenGeneric.GetHashCode();
                hash = hash * 31 + ConstructorParameters.Count.GetHashCode();
                return hash;
            }
        }
    }
}
