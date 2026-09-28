#nullable enable
using System;
using System.Collections.Generic;

namespace ZeroAlloc.Inject.Generator
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
        public List<PropertyInjectionInfo> PropertyInjections { get; }
        public List<NamedLocationInfo> NonSettableInjectProperties { get; }
        public bool HasMultipleConstructors { get; }
        public string? PrimitiveParameterName { get; }
        public string? PrimitiveParameterType { get; }
        public string? OptionalNonNullableParamName { get; }
        public string? OptionalNonNullableParamType { get; }
        public bool ImplementsDisposable { get; }
        public string? ImplementationMetadataName { get; }

        // ZAI003: the class is abstract or static; it is reported and never registered.
        public bool IsAbstractOrStatic { get; }

        // ZAI001: the class carries more than one of [Transient], [Scoped] and [Singleton].
        public bool HasMultipleLifetimes { get; }

        // ZAI004: display name of the As type when the class does not implement it, otherwise null.
        public string? AsTypeNotImplemented { get; }

        // Where each diagnostic about this class is reported. The class identifier; the lifetime
        // attribute for ZAI001 and ZAI004, set only when one of them applies; and the offending
        // constructor parameter for ZAI010 and ZAI015.
        public LocationInfo? Location { get; }
        public LocationInfo? AttributeLocation { get; }
        public LocationInfo? PrimitiveParameterLocation { get; }
        public LocationInfo? OptionalNonNullableParamLocation { get; }

        public bool IsRegistrable => !IsAbstractOrStatic && !HasMultipleLifetimes && AsTypeNotImplemented == null;

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
            string? optionalNonNullableParamName,
            string? optionalNonNullableParamType,
            bool implementsDisposable,
            string? implementationMetadataName = null,
            List<PropertyInjectionInfo>? propertyInjections = null,
            List<NamedLocationInfo>? nonSettableInjectProperties = null,
            bool isAbstractOrStatic = false,
            bool hasMultipleLifetimes = false,
            string? asTypeNotImplemented = null,
            LocationInfo? location = null,
            LocationInfo? attributeLocation = null,
            LocationInfo? primitiveParameterLocation = null,
            LocationInfo? optionalNonNullableParamLocation = null)
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
            OptionalNonNullableParamName = optionalNonNullableParamName;
            OptionalNonNullableParamType = optionalNonNullableParamType;
            ImplementsDisposable = implementsDisposable;
            ImplementationMetadataName = implementationMetadataName;
            PropertyInjections = propertyInjections ?? new List<PropertyInjectionInfo>();
            NonSettableInjectProperties = nonSettableInjectProperties ?? new List<NamedLocationInfo>();
            IsAbstractOrStatic = isAbstractOrStatic;
            HasMultipleLifetimes = hasMultipleLifetimes;
            AsTypeNotImplemented = asTypeNotImplemented;
            Location = location;
            AttributeLocation = attributeLocation;
            PrimitiveParameterLocation = primitiveParameterLocation;
            OptionalNonNullableParamLocation = optionalNonNullableParamLocation;
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
                || OptionalNonNullableParamName != other.OptionalNonNullableParamName
                || OptionalNonNullableParamType != other.OptionalNonNullableParamType
                || ImplementsDisposable != other.ImplementsDisposable
                || ImplementationMetadataName != other.ImplementationMetadataName
                || IsAbstractOrStatic != other.IsAbstractOrStatic
                || HasMultipleLifetimes != other.HasMultipleLifetimes
                || AsTypeNotImplemented != other.AsTypeNotImplemented
                || !Equals(Location, other.Location)
                || !Equals(AttributeLocation, other.AttributeLocation)
                || !Equals(PrimitiveParameterLocation, other.PrimitiveParameterLocation)
                || !Equals(OptionalNonNullableParamLocation, other.OptionalNonNullableParamLocation)
                || ConstructorParameters.Count != other.ConstructorParameters.Count
                || PropertyInjections.Count != other.PropertyInjections.Count
                || NonSettableInjectProperties.Count != other.NonSettableInjectProperties.Count
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

            for (int i = 0; i < PropertyInjections.Count; i++)
            {
                if (!PropertyInjections[i].Equals(other.PropertyInjections[i]))
                    return false;
            }

            for (int i = 0; i < NonSettableInjectProperties.Count; i++)
            {
                if (!NonSettableInjectProperties[i].Equals(other.NonSettableInjectProperties[i]))
                    return false;
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
                hash = hash * 31 + PropertyInjections.Count.GetHashCode();
                return hash;
            }
        }
    }
}
