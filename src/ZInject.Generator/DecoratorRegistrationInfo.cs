#nullable enable
using System;
using System.Collections.Generic;

namespace ZeroInject.Generator
{
    internal sealed class DecoratorRegistrationInfo : IEquatable<DecoratorRegistrationInfo>
    {
        public string TypeName { get; }
        public string DecoratorFqn { get; }
        public string? DecoratedInterfaceFqn { get; } // null = ZI011 error
        public bool IsOpenGeneric { get; }
        public List<ConstructorParameterInfo> ConstructorParameters { get; }
        public bool ImplementsDisposable { get; }
        public bool IsAbstractOrStatic { get; } // true = ZI013 warning

        public DecoratorRegistrationInfo(
            string typeName,
            string decoratorFqn,
            string? decoratedInterfaceFqn,
            bool isOpenGeneric,
            List<ConstructorParameterInfo> constructorParameters,
            bool implementsDisposable,
            bool isAbstractOrStatic)
        {
            TypeName = typeName;
            DecoratorFqn = decoratorFqn;
            DecoratedInterfaceFqn = decoratedInterfaceFqn;
            IsOpenGeneric = isOpenGeneric;
            ConstructorParameters = constructorParameters;
            ImplementsDisposable = implementsDisposable;
            IsAbstractOrStatic = isAbstractOrStatic;
        }

        public bool Equals(DecoratorRegistrationInfo? other)
        {
            if (other is null) return false;
            return DecoratorFqn == other.DecoratorFqn
                && DecoratedInterfaceFqn == other.DecoratedInterfaceFqn
                && IsOpenGeneric == other.IsOpenGeneric
                && IsAbstractOrStatic == other.IsAbstractOrStatic
                && ConstructorParameters.Count == other.ConstructorParameters.Count;
        }

        public override bool Equals(object? obj) => Equals(obj as DecoratorRegistrationInfo);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + DecoratorFqn.GetHashCode();
                hash = hash * 31 + (DecoratedInterfaceFqn?.GetHashCode() ?? 0);
                return hash;
            }
        }
    }
}
