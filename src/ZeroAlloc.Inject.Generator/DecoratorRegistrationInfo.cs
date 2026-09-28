#nullable enable
using System;
using System.Collections.Generic;

namespace ZeroAlloc.Inject.Generator
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

        // Where diagnostics about this decorator are reported: the class identifier, and the
        // [Decorator] or [DecoratorOf] attribute.
        public LocationInfo? Location { get; }
        public LocationInfo? AttributeLocation { get; }

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
            bool isDecoratorOf,
            LocationInfo? location = null,
            LocationInfo? attributeLocation = null)
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
            Location = location;
            AttributeLocation = attributeLocation;
        }

        public bool Equals(DecoratorRegistrationInfo? other)
        {
            if (other is null) return false;
            if (TypeName != other.TypeName
                || DecoratorFqn != other.DecoratorFqn
                || DecoratedInterfaceFqn != other.DecoratedInterfaceFqn
                || IsOpenGeneric != other.IsOpenGeneric
                || ImplementsDisposable != other.ImplementsDisposable
                || IsAbstractOrStatic != other.IsAbstractOrStatic
                || Order != other.Order
                || WhenRegisteredFqn != other.WhenRegisteredFqn
                || IsDecoratorOf != other.IsDecoratorOf
                || !Equals(Location, other.Location)
                || !Equals(AttributeLocation, other.AttributeLocation)
                || ConstructorParameters.Count != other.ConstructorParameters.Count)
            {
                return false;
            }

            // Each parameter is emitted into the decorator's factory, so a changed parameter type
            // must change the model.
            for (int i = 0; i < ConstructorParameters.Count; i++)
            {
                if (!ConstructorParameters[i].Equals(other.ConstructorParameters[i]))
                {
                    return false;
                }
            }

            return true;
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
