#nullable enable
using System;
using System.Collections.Immutable;
using System.Linq;

namespace ZeroAlloc.Inject.Generator
{
    /// <summary>
    /// Everything the generator's outputs are built from, with value equality, so that both the
    /// source output and the diagnostics step stay cached when an edit leaves every input equal.
    /// </summary>
    internal sealed class GeneratorInputs : IEquatable<GeneratorInputs>
    {
        public ImmutableArray<ServiceRegistrationInfo?> Transients { get; }
        public ImmutableArray<ServiceRegistrationInfo?> Scopeds { get; }
        public ImmutableArray<ServiceRegistrationInfo?> Singletons { get; }
        public ImmutableArray<string?> MethodNameOverrides { get; }
        public string AssemblyName { get; }
        public bool ContainerReferenced { get; }
        public ImmutableArray<DecoratorRegistrationInfo?> Decorators { get; }
        public ImmutableArray<ClosedGenericFactoryInfo> ClosedGenericFactories { get; }
        public GeneratedAccessibilityOption Accessibility { get; }
        public bool DependencyInjectionReferenced { get; }

        public GeneratorInputs(
            ImmutableArray<ServiceRegistrationInfo?> transients,
            ImmutableArray<ServiceRegistrationInfo?> scopeds,
            ImmutableArray<ServiceRegistrationInfo?> singletons,
            ImmutableArray<string?> methodNameOverrides,
            string assemblyName,
            bool containerReferenced,
            ImmutableArray<DecoratorRegistrationInfo?> decorators,
            ImmutableArray<ClosedGenericFactoryInfo> closedGenericFactories,
            GeneratedAccessibilityOption accessibility,
            bool dependencyInjectionReferenced)
        {
            Transients = transients;
            Scopeds = scopeds;
            Singletons = singletons;
            MethodNameOverrides = methodNameOverrides;
            AssemblyName = assemblyName;
            ContainerReferenced = containerReferenced;
            Decorators = decorators;
            ClosedGenericFactories = closedGenericFactories;
            Accessibility = accessibility;
            DependencyInjectionReferenced = dependencyInjectionReferenced;
        }

        public bool Equals(GeneratorInputs? other) =>
            other is not null
            && string.Equals(AssemblyName, other.AssemblyName, StringComparison.Ordinal)
            && ContainerReferenced == other.ContainerReferenced
            && DependencyInjectionReferenced == other.DependencyInjectionReferenced
            && Accessibility.Equals(other.Accessibility)
            && SequenceComparer<ServiceRegistrationInfo?>.Instance.Equals(Transients, other.Transients)
            && SequenceComparer<ServiceRegistrationInfo?>.Instance.Equals(Scopeds, other.Scopeds)
            && SequenceComparer<ServiceRegistrationInfo?>.Instance.Equals(Singletons, other.Singletons)
            && SequenceComparer<string?>.Instance.Equals(MethodNameOverrides, other.MethodNameOverrides)
            && SequenceComparer<DecoratorRegistrationInfo?>.Instance.Equals(Decorators, other.Decorators)
            && SequenceComparer<ClosedGenericFactoryInfo>.Instance.Equals(ClosedGenericFactories, other.ClosedGenericFactories);

        public override bool Equals(object? obj) => Equals(obj as GeneratorInputs);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + StringComparer.Ordinal.GetHashCode(AssemblyName);
                hash = hash * 31 + Transients.Length;
                hash = hash * 31 + Scopeds.Length;
                hash = hash * 31 + Singletons.Length;
                hash = hash * 31 + Decorators.Length;
                return hash;
            }
        }
    }

    /// <summary>
    /// Names of the tracked pipeline steps, which the incrementality tests read from the run result.
    /// </summary>
    internal static class TrackingNames
    {
        public const string Transients = nameof(Transients);
        public const string Scopeds = nameof(Scopeds);
        public const string Singletons = nameof(Singletons);
        public const string Decorators = nameof(Decorators);
        public const string DecoratorOfs = nameof(DecoratorOfs);
        public const string AllDecorators = nameof(AllDecorators);
        public const string ClosedGenericUsages = nameof(ClosedGenericUsages);
        public const string Inputs = nameof(Inputs);
        public const string Diagnostics = nameof(Diagnostics);
        public const string ReportedDiagnostics = nameof(ReportedDiagnostics);
    }
}
