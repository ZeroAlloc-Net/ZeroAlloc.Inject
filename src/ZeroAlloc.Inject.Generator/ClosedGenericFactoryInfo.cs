using System;
using System.Collections.Immutable;
using System.Linq;

namespace ZeroAlloc.Inject.Generator
{
    internal sealed class ClosedGenericFactoryInfo : IEquatable<ClosedGenericFactoryInfo>
    {
        public string InterfaceFqn { get; }
        public string ImplementationFqn { get; }
        public string Lifetime { get; }
        public ImmutableArray<ConstructorParameterInfo> Parameters { get; }
        public bool ImplementsDisposable { get; }

        public ClosedGenericFactoryInfo(
            string interfaceFqn,
            string implementationFqn,
            string lifetime,
            ImmutableArray<ConstructorParameterInfo> parameters,
            bool implementsDisposable)
        {
            InterfaceFqn = interfaceFqn;
            ImplementationFqn = implementationFqn;
            Lifetime = lifetime;
            Parameters = parameters;
            ImplementsDisposable = implementsDisposable;
        }

        public bool Equals(ClosedGenericFactoryInfo? other)
        {
            if (other is null) return false;
            return string.Equals(InterfaceFqn, other.InterfaceFqn, StringComparison.Ordinal)
                && string.Equals(ImplementationFqn, other.ImplementationFqn, StringComparison.Ordinal)
                && string.Equals(Lifetime, other.Lifetime, StringComparison.Ordinal)
                && Parameters.SequenceEqual(other.Parameters)
                && ImplementsDisposable == other.ImplementsDisposable;
        }

        public override bool Equals(object? obj) => Equals(obj as ClosedGenericFactoryInfo);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + InterfaceFqn.GetHashCode();
                hash = hash * 31 + ImplementationFqn.GetHashCode();
                hash = hash * 31 + Lifetime.GetHashCode();
                hash = hash * 31 + ImplementsDisposable.GetHashCode();
                return hash;
            }
        }
    }
}
