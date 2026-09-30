using System;
using System.Collections.Immutable;
using System.Linq;

namespace ZeroAlloc.Inject.Generator
{
    /// <summary>
    /// One registration of a closed form of an open generic service that a constructor asks for,
    /// such as <c>IRepo&lt;int&gt;</c> implemented by <c>Repo&lt;int&gt;</c>. A closed form has one of
    /// these per open generic registration of its service and key, in registration order.
    /// </summary>
    internal sealed class ClosedGenericFactoryInfo : IEquatable<ClosedGenericFactoryInfo>
    {
        /// <summary>The closed service type, such as <c>global::Ns.IRepo&lt;int&gt;</c>.</summary>
        public string InterfaceFqn { get; }

        /// <summary>The closed implementation type, such as <c>global::Ns.Repo&lt;int&gt;</c>.</summary>
        public string ImplementationFqn { get; }

        /// <summary>
        /// The open service type the registration was made for, in the unbound form, such as
        /// <c>global::Ns.IRepo&lt;&gt;</c>. It is the implementation type itself for the concrete
        /// registration. Decorators are looked up by it.
        /// </summary>
        public string OpenServiceFqn { get; }

        /// <summary>The key of a keyed registration, or null.</summary>
        public string? Key { get; }

        public string Lifetime { get; }
        public ImmutableArray<ConstructorParameterInfo> Parameters { get; }
        public bool ImplementsDisposable { get; }

        /// <summary>
        /// True for the registration that resolving the closed form returns: the one Microsoft DI
        /// picks among the open generic registrations of its service and key, the last one added.
        /// </summary>
        public bool IsResolved { get; }

        /// <summary>
        /// True when a type argument is a value type. Microsoft DI refuses to close an open generic
        /// over one when dynamic code is not supported, as under NativeAOT.
        /// </summary>
        public bool HasValueTypeArgument { get; }

        public ClosedGenericFactoryInfo(
            string interfaceFqn,
            string implementationFqn,
            string openServiceFqn,
            string? key,
            string lifetime,
            ImmutableArray<ConstructorParameterInfo> parameters,
            bool implementsDisposable,
            bool isResolved,
            bool hasValueTypeArgument)
        {
            InterfaceFqn = interfaceFqn;
            ImplementationFqn = implementationFqn;
            OpenServiceFqn = openServiceFqn;
            Key = key;
            Lifetime = lifetime;
            Parameters = parameters;
            ImplementsDisposable = implementsDisposable;
            IsResolved = isResolved;
            HasValueTypeArgument = hasValueTypeArgument;
        }

        public bool Equals(ClosedGenericFactoryInfo? other)
        {
            if (other is null) return false;
            return string.Equals(InterfaceFqn, other.InterfaceFqn, StringComparison.Ordinal)
                && string.Equals(ImplementationFqn, other.ImplementationFqn, StringComparison.Ordinal)
                && string.Equals(OpenServiceFqn, other.OpenServiceFqn, StringComparison.Ordinal)
                && string.Equals(Key, other.Key, StringComparison.Ordinal)
                && string.Equals(Lifetime, other.Lifetime, StringComparison.Ordinal)
                && Parameters.SequenceEqual(other.Parameters)
                && ImplementsDisposable == other.ImplementsDisposable
                && IsResolved == other.IsResolved
                && HasValueTypeArgument == other.HasValueTypeArgument;
        }

        public override bool Equals(object? obj) => Equals(obj as ClosedGenericFactoryInfo);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + InterfaceFqn.GetHashCode();
                hash = hash * 31 + ImplementationFqn.GetHashCode();
                hash = hash * 31 + OpenServiceFqn.GetHashCode();
                hash = hash * 31 + (Key?.GetHashCode() ?? 0);
                hash = hash * 31 + Lifetime.GetHashCode();
                hash = hash * 31 + ImplementsDisposable.GetHashCode();
                hash = hash * 31 + IsResolved.GetHashCode();
                hash = hash * 31 + HasValueTypeArgument.GetHashCode();
                return hash;
            }
        }
    }
}
