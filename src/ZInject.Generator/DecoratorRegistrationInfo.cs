#nullable enable
using System;
using System.Collections.Generic;

namespace ZInject.Generator
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
        public int Order { get; }
        public string? WhenRegisteredFqn { get; } // null = unconditional
        public bool IsDecoratorOf { get; } // true = [DecoratorOf], false = [Decorator]

        public DecoratorRegistrationInfo(
            string typeName,
            string decoratorFqn,
            string? decoratedInterfaceFqn,
            bool isOpenGeneric,
            List<ConstructorParameterInfo> constructorParameters,
            bool implementsDisposable,
            bool isAbstractOrStatic,
            int order,
            string? whenRegisteredFqn,
            bool isDecoratorOf)
        {
            TypeName = typeName;
            DecoratorFqn = decoratorFqn;
            DecoratedInterfaceFqn = decoratedInterfaceFqn;
            IsOpenGeneric = isOpenGeneric;
            ConstructorParameters = constructorParameters;
            ImplementsDisposable = implementsDisposable;
            IsAbstractOrStatic = isAbstractOrStatic;
            Order = order;
            WhenRegisteredFqn = whenRegisteredFqn;
            IsDecoratorOf = isDecoratorOf;
        }

        public bool Equals(DecoratorRegistrationInfo? other)
        {
            if (other is null) return false;
            return DecoratorFqn == other.DecoratorFqn
                && DecoratedInterfaceFqn == other.DecoratedInterfaceFqn
                && IsOpenGeneric == other.IsOpenGeneric
                && IsAbstractOrStatic == other.IsAbstractOrStatic
                && ConstructorParameters.Count == other.ConstructorParameters.Count
                && Order == other.Order
                && WhenRegisteredFqn == other.WhenRegisteredFqn
                && IsDecoratorOf == other.IsDecoratorOf;
        }

        public override bool Equals(object? obj) => Equals(obj as DecoratorRegistrationInfo);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + DecoratorFqn.GetHashCode();
                hash = hash * 31 + (DecoratedInterfaceFqn?.GetHashCode() ?? 0);
                hash = hash * 31 + (WhenRegisteredFqn?.GetHashCode() ?? 0);
                hash = hash * 31 + Order.GetHashCode();
                hash = hash * 31 + IsDecoratorOf.GetHashCode();
                return hash;
            }
        }
    }
}
