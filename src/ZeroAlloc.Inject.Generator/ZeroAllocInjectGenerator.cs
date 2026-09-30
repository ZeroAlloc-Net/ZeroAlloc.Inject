#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ZeroAlloc.Inject.Generator
{
    [Generator]
    public sealed class ZeroAllocInjectGenerator : IIncrementalGenerator
    {
        private static readonly SymbolDisplayFormat FullyQualifiedFormat =
            SymbolDisplayFormat.FullyQualifiedFormat;

        private static readonly HashSet<string> FilteredInterfaces = new HashSet<string>
        {
            "System.IDisposable",
            "System.IAsyncDisposable",
            "System.IComparable",
            "System.IFormattable",
            "System.ICloneable",
            "System.IConvertible"
        };

        // Also filter generic versions like IComparable<T>, IEquatable<T>
        private static readonly HashSet<string> FilteredGenericInterfaces = new HashSet<string>
        {
            "System.IComparable",
            "System.IEquatable"
        };

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var transients = context.SyntaxProvider.ForAttributeWithMetadataName(
                "ZeroAlloc.Inject.TransientAttribute",
                predicate: static (node, _) => true,
                transform: static (ctx, ct) => GetServiceInfo(ctx, "Transient", ct))
                .Where(static x => x != null)
                .Collect()
                .WithTrackingName(TrackingNames.Transients);

            var scopeds = context.SyntaxProvider.ForAttributeWithMetadataName(
                "ZeroAlloc.Inject.ScopedAttribute",
                predicate: static (node, _) => true,
                transform: static (ctx, ct) => GetServiceInfo(ctx, "Scoped", ct))
                .Where(static x => x != null)
                .Collect()
                .WithTrackingName(TrackingNames.Scopeds);

            var singletons = context.SyntaxProvider.ForAttributeWithMetadataName(
                "ZeroAlloc.Inject.SingletonAttribute",
                predicate: static (node, _) => true,
                transform: static (ctx, ct) => GetServiceInfo(ctx, "Singleton", ct))
                .Where(static x => x != null)
                .Collect()
                .WithTrackingName(TrackingNames.Singletons);

            var assemblyAttr = context.SyntaxProvider.ForAttributeWithMetadataName(
                "ZeroAlloc.Inject.ZeroAllocInjectAttribute",
                predicate: static (node, _) => true,
                transform: static (ctx, ct) =>
                {
                    var attr = ctx.Attributes.FirstOrDefault();
                    if (attr != null && attr.ConstructorArguments.Length > 0)
                    {
                        var val = attr.ConstructorArguments[0].Value as string;
                        if (val != null)
                        {
                            return val;
                        }
                    }
                    return (string?)null;
                })
                .Where(static x => x != null)
                .Collect();

            var assemblyName = context.CompilationProvider.Select(
                static (compilation, _) => compilation.AssemblyName ?? "Assembly");

            var accessibilityOption = context.AnalyzerConfigOptionsProvider.Select(
                static (provider, _) => ResolveGeneratedAccessibility(provider));

            var hasContainer = context.CompilationProvider.Select(
                static (compilation, _) =>
                {
                    foreach (var asm in compilation.ReferencedAssemblyNames)
                    {
                        if (asm.Name == "ZeroAlloc.Inject.Container")
                        {
                            return true;
                        }
                    }
                    return false;
                });

            // ZAI008: every generated file uses IServiceCollection, so without the DI abstractions
            // assembly the generated code cannot compile. GetTypesByMetadataName, unlike
            // GetTypeByMetadataName, still finds the type when two references both define it.
            var hasDependencyInjectionAbstractions = context.CompilationProvider.Select(
                static (compilation, _) => !compilation.GetTypesByMetadataName(
                    "Microsoft.Extensions.DependencyInjection.IServiceCollection").IsEmpty);

            var decorators = context.SyntaxProvider.ForAttributeWithMetadataName(
                "ZeroAlloc.Inject.DecoratorAttribute",
                predicate: static (node, _) => true,
                transform: static (ctx, ct) => GetDecoratorInfo(ctx, ct))
                .Where(static x => x != null)
                .Collect()
                .WithTrackingName(TrackingNames.Decorators);

            var decoratorOfs = context.SyntaxProvider.ForAttributeWithMetadataName(
                "ZeroAlloc.Inject.DecoratorOfAttribute",
                predicate: static (node, _) => true,
                transform: static (ctx, ct) => GetDecoratorOfInfo(ctx, ct))
                .Where(static x => x != null)
                .Collect()
                .WithTrackingName(TrackingNames.DecoratorOfs);

            var allDecorators = decorators.Combine(decoratorOfs)
                .Select(static (pair, _) =>
                {
                    var builder = ImmutableArray.CreateBuilder<DecoratorRegistrationInfo?>();
                    builder.AddRange(pair.Left);
                    builder.AddRange(pair.Right);
                    return builder.ToImmutable();
                })
                .WithComparer(SequenceComparer<DecoratorRegistrationInfo?>.Instance)
                .WithTrackingName(TrackingNames.AllDecorators);

            var closedGenericUsages = transients
                .Combine(scopeds)
                .Combine(singletons)
                .Combine(allDecorators)
                .Combine(context.CompilationProvider)
                .Select(static (data, ct) => FindClosedGenericUsages(data, ct))
                // The step reruns on every compilation. Comparing the result by content, not by array
                // reference, keeps everything downstream cached when the usages did not change.
                .WithComparer(SequenceComparer<ClosedGenericFactoryInfo>.Instance)
                .WithTrackingName(TrackingNames.ClosedGenericUsages);

            var inputs = transients
                .Combine(scopeds)
                .Combine(singletons)
                .Combine(assemblyAttr)
                .Combine(assemblyName)
                .Combine(hasContainer)
                .Combine(allDecorators)
                .Combine(closedGenericUsages)
                .Combine(accessibilityOption)
                .Combine(hasDependencyInjectionAbstractions)
                .Select(static (all, _) => new GeneratorInputs(
                    transients: all.Left.Left.Left.Left.Left.Left.Left.Left.Left,
                    scopeds: all.Left.Left.Left.Left.Left.Left.Left.Left.Right,
                    singletons: all.Left.Left.Left.Left.Left.Left.Left.Right,
                    methodNameOverrides: all.Left.Left.Left.Left.Left.Left.Right,
                    assemblyName: all.Left.Left.Left.Left.Left.Right,
                    containerReferenced: all.Left.Left.Left.Left.Right,
                    decorators: all.Left.Left.Left.Right,
                    closedGenericFactories: all.Left.Left.Right,
                    accessibility: all.Left.Right,
                    dependencyInjectionReferenced: all.Right))
                .WithTrackingName(TrackingNames.Inputs);

            context.RegisterSourceOutput(inputs, static (spc, input) => GenerateSources(spc, input));

            // Diagnostics are computed from the cached models into value-equal DiagnosticInfo records.
            // Only the last step touches the compilation: it binds each LocationInfo to its syntax tree,
            // so the reported location is a source location that #pragma warning disable can suppress.
            // On an edit that changes no diagnostic, both steps compare equal and the output stays cached.
            var diagnostics = inputs
                .Select(static (input, _) => CollectDiagnostics(input))
                .WithComparer(SequenceComparer<DiagnosticInfo>.Instance)
                .WithTrackingName(TrackingNames.Diagnostics);

            var reportedDiagnostics = diagnostics
                .Combine(context.CompilationProvider)
                .Select(static (pair, _) => ToDiagnostics(pair.Left, pair.Right))
                .WithComparer(new SequenceComparer<Diagnostic>(ReportedDiagnosticComparer.Instance))
                .WithTrackingName(TrackingNames.ReportedDiagnostics);

            context.RegisterSourceOutput(reportedDiagnostics, static (spc, reported) =>
            {
                foreach (var diagnostic in reported)
                {
                    spc.ReportDiagnostic(diagnostic);
                }
            });
        }

        private static void GenerateSources(SourceProductionContext spc, GeneratorInputs input)
        {
            var allServices = new List<ServiceRegistrationInfo>();
            AddRegistrable(allServices, input.Transients, null, null);
            AddRegistrable(allServices, input.Scopeds, null, null);
            AddRegistrable(allServices, input.Singletons, null, null);

            if (allServices.Count == 0)
            {
                return;
            }

            var decoratorsByInterface = GroupDecorators(ValidDecorators(allServices, input.Decorators, null));

            string? methodNameOverride = null;
            if (input.MethodNameOverrides.Length > 0)
            {
                methodNameOverride = input.MethodNameOverrides[0];
            }

            var asmName = input.AssemblyName;
            var accessibility = input.Accessibility;

            var source = GenerateExtensionClass(allServices, asmName, methodNameOverride, decoratorsByInterface, accessibility.Keyword, input.ClosedGenericFactories);
            spc.AddSource("ZeroAlloc.Inject.ServiceCollectionExtensions.g.cs", source);

            if (input.ContainerReferenced)
            {
                var providerSource = GenerateServiceProviderClass(allServices, asmName, decoratorsByInterface, accessibility.Keyword, input.ClosedGenericFactories);
                spc.AddSource("ZeroAlloc.Inject.ServiceProvider.g.cs", providerSource);

                var standaloneCode = GenerateStandaloneServiceProviderClass(allServices, asmName, decoratorsByInterface, input.ClosedGenericFactories);
                spc.AddSource(asmName + ".StandaloneServiceProvider.g.cs", standaloneCode);
            }
        }

        /// <summary>
        /// Every ZAI diagnostic for this run. Each carries the most precise source location its models
        /// hold. Only ZAI008 and ZAI020 have none: they are about the project's references and an
        /// MSBuild property, not about any class.
        /// </summary>
        private static ImmutableArray<DiagnosticInfo> CollectDiagnostics(GeneratorInputs input)
        {
            var diagnostics = new List<DiagnosticInfo>();

            if (input.Accessibility.InvalidValue != null)
            {
                diagnostics.Add(new DiagnosticInfo(
                    DiagnosticDescriptors.InvalidGeneratedAccessibility,
                    null,
                    input.Accessibility.InvalidValue));
            }

            // ZAI001, ZAI003 and ZAI004 are reported here, once per class, and the class is left
            // out of every generated registration.
            var allServices = new List<ServiceRegistrationInfo>();
            var reportedClasses = new HashSet<string>(StringComparer.Ordinal);
            AddRegistrable(allServices, input.Transients, reportedClasses, diagnostics);
            AddRegistrable(allServices, input.Scopeds, reportedClasses, diagnostics);
            AddRegistrable(allServices, input.Singletons, reportedClasses, diagnostics);

            foreach (var svc in allServices)
            {
                if (!svc.HasPublicConstructor)
                {
                    diagnostics.Add(new DiagnosticInfo(
                        DiagnosticDescriptors.NoPublicConstructor, svc.Location, svc.TypeName));
                }

                if (svc.Interfaces.Count == 0 && svc.AsType == null)
                {
                    diagnostics.Add(new DiagnosticInfo(
                        DiagnosticDescriptors.NoInterfaces, svc.Location, svc.TypeName));
                }

                if (svc.HasMultipleConstructors)
                {
                    diagnostics.Add(new DiagnosticInfo(
                        DiagnosticDescriptors.MultipleConstructorsNoAttribute, svc.Location, svc.TypeName));
                }

                if (svc.PrimitiveParameterName != null)
                {
                    diagnostics.Add(new DiagnosticInfo(
                        DiagnosticDescriptors.PrimitiveConstructorParameter,
                        svc.PrimitiveParameterLocation ?? svc.Location,
                        svc.PrimitiveParameterName,
                        svc.TypeName,
                        svc.PrimitiveParameterType));
                }

                if (svc.OptionalNonNullableParamName != null)
                {
                    diagnostics.Add(new DiagnosticInfo(
                        DiagnosticDescriptors.OptionalDependencyOnNonNullable,
                        svc.OptionalNonNullableParamLocation ?? svc.Location,
                        svc.OptionalNonNullableParamName,
                        svc.TypeName,
                        svc.OptionalNonNullableParamType));
                }

                foreach (var prop in svc.NonSettableInjectProperties)
                {
                    diagnostics.Add(new DiagnosticInfo(
                        DiagnosticDescriptors.InjectOnNonSettableProperty,
                        prop.Location ?? svc.Location,
                        prop.Name,
                        svc.TypeName));
                }
            }

            if (allServices.Count == 0 && input.Decorators.Length == 0)
            {
                return diagnostics.ToImmutableArray();
            }

            var decoratorsByInterface = GroupDecorators(ValidDecorators(allServices, input.Decorators, diagnostics));

            // ZAI017: decorators of one interface are sorted by Order, so a duplicate is adjacent.
            // It is reported at the later attribute in source, with the earlier one as additional.
            foreach (var kvp in decoratorsByInterface)
            {
                var list = kvp.Value;
                for (int i = 0; i < list.Count - 1; i++)
                {
                    if (list[i].IsDecoratorOf && list[i + 1].IsDecoratorOf && list[i].Order == list[i + 1].Order)
                    {
                        var earlier = list[i].AttributeLocation;
                        var later = list[i + 1].AttributeLocation;
                        if (IsBefore(later, earlier))
                        {
                            (earlier, later) = (later, earlier);
                        }

                        diagnostics.Add(new DiagnosticInfo(
                            DiagnosticDescriptors.DecoratorOfDuplicateOrder,
                            later,
                            earlier == null ? ImmutableArray<LocationInfo>.Empty : ImmutableArray.Create(earlier),
                            kvp.Key,
                            list[i].Order.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            list[i].TypeName,
                            list[i + 1].TypeName));
                    }
                }
            }

            DetectCircularDependencies(diagnostics, allServices, decoratorsByInterface);

            // ZAI018: warn when an open generic has no detected closed usages
            {
                var closedFqnSet = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
                foreach (var cgf in input.ClosedGenericFactories)
                    closedFqnSet.Add(cgf.InterfaceFqn);

                foreach (var svc in allServices)
                {
                    if (!svc.IsOpenGeneric) continue;

                    // Without As, the concrete type is registered too, so a closed usage of it counts.
                    var ifaces = GetServiceTypes(svc);

                    bool anyUsage = false;
                    foreach (var iface in ifaces)
                    {
                        var prefix = iface.IndexOf('<') >= 0
                            ? iface.Substring(0, iface.IndexOf('<'))
                            : iface;
                        foreach (var fqn in closedFqnSet)
                        {
                            if (fqn.Length > prefix.Length
                                && fqn.StartsWith(prefix, StringComparison.Ordinal)
                                && fqn[prefix.Length] == '<')
                            {
                                anyUsage = true;
                                break;
                            }
                        }
                        if (anyUsage) break;
                    }

                    if (!anyUsage)
                    {
                        diagnostics.Add(new DiagnosticInfo(
                            DiagnosticDescriptors.NoDetectedClosedUsages, svc.Location, svc.TypeName));
                    }
                }
            }

            if (allServices.Count > 0 && !input.DependencyInjectionReferenced)
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.MissingDIAbstractions, null));
            }

            return diagnostics.ToImmutableArray();
        }

        private static ImmutableArray<Diagnostic> ToDiagnostics(ImmutableArray<DiagnosticInfo> infos, Compilation compilation)
        {
            if (infos.IsEmpty)
            {
                return ImmutableArray<Diagnostic>.Empty;
            }

            var trees = new SyntaxTreeLookup(compilation);
            var builder = ImmutableArray.CreateBuilder<Diagnostic>(infos.Length);
            foreach (var info in infos)
            {
                builder.Add(info.ToDiagnostic(trees));
            }
            return builder.MoveToImmutable();
        }

        private static bool IsBefore(LocationInfo? a, LocationInfo? b)
        {
            if (a == null || b == null)
            {
                return false;
            }

            int byFile = string.CompareOrdinal(a.FilePath, b.FilePath);
            return byFile != 0 ? byFile < 0 : a.Span.Start < b.Span.Start;
        }

        /// <summary>
        /// The decorators that can be applied. With a diagnostics list, each rejected decorator is
        /// reported: ZAI013 and ZAI011 at the class, ZAI016 and ZAI012 at the attribute.
        /// </summary>
        private static List<DecoratorRegistrationInfo> ValidDecorators(
            List<ServiceRegistrationInfo> allServices,
            ImmutableArray<DecoratorRegistrationInfo?> decoratorInfos,
            List<DiagnosticInfo>? diagnostics)
        {
            // Build lookup of registered interface FQNs for ZI012 check
            var registeredInterfaces = new System.Collections.Generic.HashSet<string>();
            foreach (var svc in allServices)
            {
                foreach (var iface in svc.Interfaces)
                    registeredInterfaces.Add(iface);
                if (svc.AsType != null)
                    registeredInterfaces.Add(svc.AsType);
            }

            var validDecorators = new System.Collections.Generic.List<DecoratorRegistrationInfo>();
            foreach (var dec in decoratorInfos)
            {
                if (dec == null) continue;
                if (dec.IsAbstractOrStatic)
                {
                    diagnostics?.Add(new DiagnosticInfo(
                        DiagnosticDescriptors.DecoratorOnAbstractOrStatic, dec.Location, dec.TypeName));
                    continue;
                }
                if (dec.DecoratedInterfaceFqn == null)
                {
                    if (dec.IsDecoratorOf)
                    {
                        diagnostics?.Add(new DiagnosticInfo(
                            DiagnosticDescriptors.DecoratorOfInterfaceNotImplemented,
                            dec.AttributeLocation ?? dec.Location, dec.TypeName, dec.DecoratorFqn));
                    }
                    else
                    {
                        diagnostics?.Add(new DiagnosticInfo(
                            DiagnosticDescriptors.DecoratorNoMatchingInterface, dec.Location, dec.TypeName));
                    }
                    continue;
                }
                if (!registeredInterfaces.Contains(dec.DecoratedInterfaceFqn))
                {
                    diagnostics?.Add(new DiagnosticInfo(
                        DiagnosticDescriptors.DecoratorNoRegisteredInner,
                        dec.AttributeLocation ?? dec.Location, dec.TypeName, dec.DecoratedInterfaceFqn));
                    continue;
                }
                validDecorators.Add(dec);
            }

            return validDecorators;
        }

        /// <summary>Decorators by decorated interface FQN, each list sorted by Order ascending.</summary>
        private static Dictionary<string, List<DecoratorRegistrationInfo>> GroupDecorators(
            List<DecoratorRegistrationInfo> validDecorators)
        {
            var decoratorsByInterface = new Dictionary<string, List<DecoratorRegistrationInfo>>();
            foreach (var dec in validDecorators)
            {
                if (!decoratorsByInterface.TryGetValue(dec.DecoratedInterfaceFqn!, out var list))
                {
                    list = new List<DecoratorRegistrationInfo>();
                    decoratorsByInterface[dec.DecoratedInterfaceFqn!] = list;
                }
                list.Add(dec);
            }

            foreach (var list in decoratorsByInterface.Values)
            {
                list.Sort(static (a, b) => a.Order.CompareTo(b.Order));
            }

            return decoratorsByInterface;
        }

        /// <summary>
        /// Resolves the org-wide "ZeroAllocGeneratedAccessibility" MSBuild property into the C#
        /// accessibility keyword the generator should emit for every public entry point it produces
        /// (the MS DI extension method, and the hybrid container's extension method/factory).
        /// </summary>
        private static GeneratedAccessibilityOption ResolveGeneratedAccessibility(AnalyzerConfigOptionsProvider provider)
        {
            var hasValue = provider.GlobalOptions.TryGetValue(
                "build_property.ZeroAllocGeneratedAccessibility", out var raw);

            if (!hasValue || string.IsNullOrEmpty(raw) || string.Equals(raw, "Public", StringComparison.OrdinalIgnoreCase))
            {
                return new GeneratedAccessibilityOption("public", null);
            }

            if (string.Equals(raw, "Internal", StringComparison.OrdinalIgnoreCase))
            {
                return new GeneratedAccessibilityOption("internal", null);
            }

            // Invalid value: fall back to the safe default (Public) and let the caller report ZAI020.
            return new GeneratedAccessibilityOption("public", raw);
        }

        /// <summary>
        /// Adds the registrable services to the list. With a diagnostics list, each class that cannot
        /// be registered is reported once: ZAI003 at the class, ZAI001 and ZAI004 at the attribute.
        /// </summary>
        private static void AddRegistrable(
            List<ServiceRegistrationInfo> list,
            ImmutableArray<ServiceRegistrationInfo?> items,
            HashSet<string>? reportedClasses,
            List<DiagnosticInfo>? diagnostics)
        {
            foreach (var item in items)
            {
                if (item == null)
                {
                    continue;
                }

                if (item.IsRegistrable)
                {
                    list.Add(item);
                    continue;
                }

                // A class with several lifetime attributes arrives once per attribute: report it once.
                if (diagnostics == null || reportedClasses == null || !reportedClasses.Add(item.FullyQualifiedName))
                {
                    continue;
                }

                if (item.IsAbstractOrStatic)
                {
                    diagnostics.Add(new DiagnosticInfo(
                        DiagnosticDescriptors.AttributeOnAbstractOrStatic, item.Location, item.TypeName));
                }
                else if (item.HasMultipleLifetimes)
                {
                    diagnostics.Add(new DiagnosticInfo(
                        DiagnosticDescriptors.MultipleLifetimeAttributes,
                        item.AttributeLocation ?? item.Location,
                        item.TypeName));
                }
                else
                {
                    diagnostics.Add(new DiagnosticInfo(
                        DiagnosticDescriptors.AsTypeNotImplemented,
                        item.AttributeLocation ?? item.Location,
                        item.TypeName,
                        item.AsTypeNotImplemented));
                }
            }
        }

        private static readonly HashSet<string> LifetimeAttributeNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "ZeroAlloc.Inject.TransientAttribute",
            "ZeroAlloc.Inject.ScopedAttribute",
            "ZeroAlloc.Inject.SingletonAttribute",
        };

        /// <summary>
        /// ZAI001: the second lifetime attribute of the class, or null when it has at most one of
        /// [Transient], [Scoped] and [Singleton]. Every lifetime pipeline finds the same attribute, so
        /// the class is reported at one place whichever pipeline reports it.
        /// </summary>
        private static AttributeData? FindSecondLifetimeAttribute(INamedTypeSymbol typeSymbol)
        {
            int count = 0;
            foreach (var attr in typeSymbol.GetAttributes())
            {
                var name = attr.AttributeClass?.ToDisplayString();
                if (name != null && LifetimeAttributeNames.Contains(name) && ++count > 1)
                {
                    return attr;
                }
            }
            return null;
        }

        /// <summary>
        /// ZAI004: whether the class can be registered as the As type: As is the class itself, one of
        /// its base classes, or one of its interfaces. A generic As, such as typeof(IRepo&lt;&gt;), is
        /// compared by its generic definition, the same way the generator emits it.
        /// </summary>
        private static bool ImplementsAsType(INamedTypeSymbol typeSymbol, INamedTypeSymbol asSymbol)
        {
            if (asSymbol.TypeKind == TypeKind.Error)
            {
                // The compiler already reports the unresolved type.
                return true;
            }

            bool byDefinition = asSymbol.IsGenericType;
            var target = byDefinition ? asSymbol.OriginalDefinition : asSymbol;

            for (var current = typeSymbol; current != null; current = current.BaseType)
            {
                if (Matches(current, target, byDefinition))
                {
                    return true;
                }
            }

            foreach (var iface in typeSymbol.AllInterfaces)
            {
                if (Matches(iface, target, byDefinition))
                {
                    return true;
                }
            }

            return false;

            static bool Matches(INamedTypeSymbol candidate, INamedTypeSymbol target, bool byDefinition) =>
                SymbolEqualityComparer.Default.Equals(byDefinition ? candidate.OriginalDefinition : candidate, target);
        }

        /// <summary>
        /// Converts a fully qualified generic type string like "global::Ns.Foo&lt;T, U&gt;" to
        /// the unbound generic form "global::Ns.Foo&lt;,&gt;" suitable for use in typeof() expressions.
        /// </summary>
        private static bool ContainsTypeParameter(ITypeSymbol type) => type switch
        {
            ITypeParameterSymbol => true,
            IArrayTypeSymbol array => ContainsTypeParameter(array.ElementType),
            IPointerTypeSymbol pointer => ContainsTypeParameter(pointer.PointedAtType),
            INamedTypeSymbol named => named.TypeArguments.Any(ContainsTypeParameter)
                || (named.ContainingType is { } outer && ContainsTypeParameter(outer)),
            _ => false,
        };

        /// <summary>
        /// Encodes the type arguments of <paramref name="closedType"/> as documentation-comment
        /// reference IDs, resolved again by <c>FindClosedGenericUsages</c>. A metadata name cannot
        /// do this: <c>int?</c> has the metadata name <c>System.Nullable`1</c>, which resolves to the
        /// open <c>Nullable&lt;T&gt;</c> and emitted <c>Repo&lt;T?&gt;</c>, and a nested type or an array
        /// did not resolve at all, so its closed form was silently left out of the container.
        /// </summary>
        private static ImmutableArray<string> ToTypeArgumentReferenceIds(INamedTypeSymbol closedType)
        {
            var builder = ImmutableArray.CreateBuilder<string>(closedType.TypeArguments.Length);
            foreach (var typeArgument in closedType.TypeArguments)
                builder.Add(DocumentationCommentId.CreateReferenceId(typeArgument));
            return builder.MoveToImmutable();
        }

        private static string ToUnboundGenericString(string fullyQualifiedName, int arity)
        {
            var idx = fullyQualifiedName.IndexOf('<');
            if (idx < 0)
            {
                return fullyQualifiedName;
            }
            var prefix = fullyQualifiedName.Substring(0, idx);
            // Build <,,,> with (arity-1) commas
            var commas = arity > 1 ? new string(',', arity - 1) : "";
            return prefix + "<" + commas + ">";
        }

        private static ServiceRegistrationInfo? GetServiceInfo(
            GeneratorAttributeSyntaxContext ctx,
            string lifetime,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            var typeSymbol = ctx.TargetSymbol as INamedTypeSymbol;
            if (typeSymbol == null)
            {
                return null;
            }

            var ns = typeSymbol.ContainingNamespace.IsGlobalNamespace
                ? ""
                : typeSymbol.ContainingNamespace.ToDisplayString();

            var fullyQualifiedName = typeSymbol.ToDisplayString(FullyQualifiedFormat);
            if (typeSymbol.IsGenericType)
            {
                fullyQualifiedName = ToUnboundGenericString(fullyQualifiedName, typeSymbol.TypeParameters.Length);
            }
            var typeName = typeSymbol.Name;
            var location = GetClassLocation(ctx, typeSymbol);

            if (typeSymbol.IsAbstract || typeSymbol.IsStatic)
            {
                // ZAI003: nothing else about the class matters, it can never be instantiated.
                return new ServiceRegistrationInfo(
                    ns, typeName, fullyQualifiedName, lifetime, new List<string>(), null, null, false,
                    false, null, false, new List<ConstructorParameterInfo>(), false, null, null, null, null,
                    false, isAbstractOrStatic: true, location: location);
            }

            // ZAI001 points at the second lifetime attribute, the one that makes the set invalid.
            var secondLifetimeAttribute = FindSecondLifetimeAttribute(typeSymbol);
            bool hasMultipleLifetimes = secondLifetimeAttribute != null;
            LocationInfo? attributeLocation = hasMultipleLifetimes
                ? LocationInfo.From(secondLifetimeAttribute!.ApplicationSyntaxReference)
                : null;

            // Extract attribute properties
            string? asType = null;
            string? asTypeNotImplemented = null;
            string? key = null;
            bool allowMultiple = false;

            var attr = ctx.Attributes.FirstOrDefault();
            if (attr != null)
            {
                foreach (var named in attr.NamedArguments)
                {
                    if (named.Key == "As" && named.Value.Value is INamedTypeSymbol asSymbol)
                    {
                        asType = asSymbol.ToDisplayString(FullyQualifiedFormat);
                        if (!ImplementsAsType(typeSymbol, asSymbol))
                        {
                            asTypeNotImplemented = asSymbol.ToDisplayString();
                            // ZAI004 points at the attribute that carries As. ZAI001 wins when both
                            // apply, as the class is reported once.
                            attributeLocation ??= LocationInfo.From(attr.ApplicationSyntaxReference);
                        }
                        if (asSymbol.IsGenericType)
                        {
                            asType = ToUnboundGenericString(asType, asSymbol.TypeParameters.Length);
                        }
                    }
                    else if (named.Key == "Key" && named.Value.Value is string keyValue)
                    {
                        key = keyValue;
                    }
                    else if (named.Key == "AllowMultiple" && named.Value.Value is bool allowValue)
                    {
                        allowMultiple = allowValue;
                    }
                }
            }

            // Collect interfaces, filtering out well-known system interfaces
            var interfaces = new List<string>();
            foreach (var iface in typeSymbol.AllInterfaces)
            {
                var ifaceFullName = iface.ToDisplayString();
                var ifaceOriginal = iface.OriginalDefinition.ToDisplayString();

                if (FilteredInterfaces.Contains(ifaceFullName))
                {
                    continue;
                }

                if (FilteredInterfaces.Contains(ifaceOriginal))
                {
                    continue;
                }

                // Check generic filtered interfaces (e.g., IComparable<T>, IEquatable<T>)
                bool filtered = false;
                if (iface.IsGenericType)
                {
                    var originalName = iface.OriginalDefinition.ContainingNamespace + "." + iface.OriginalDefinition.Name;
                    if (FilteredGenericInterfaces.Contains(originalName))
                    {
                        filtered = true;
                    }
                }

                if (filtered)
                {
                    continue;
                }

                var ifaceDisplay = iface.ToDisplayString(FullyQualifiedFormat);
                if (typeSymbol.IsGenericType && iface.IsGenericType)
                {
                    ifaceDisplay = ToUnboundGenericString(ifaceDisplay, iface.TypeArguments.Length);
                }
                interfaces.Add(ifaceDisplay);
            }

            // Detect IDisposable / IAsyncDisposable
            bool implementsDisposable = false;
            foreach (var iface in typeSymbol.AllInterfaces)
            {
                var name = iface.ToDisplayString();
                if (name == "System.IDisposable" || name == "System.IAsyncDisposable")
                {
                    implementsDisposable = true;
                    break;
                }
            }

            // Detect open generics
            bool isOpenGeneric = typeSymbol.IsGenericType;
            string? openGenericArity = null;
            if (isOpenGeneric)
            {
                openGenericArity = typeSymbol.TypeParameters.Length.ToString();
            }

            // Constructor analysis for factory lambda generation
            var publicCtors = new List<IMethodSymbol>();
            foreach (var ctor in typeSymbol.InstanceConstructors)
            {
                if (ctor.DeclaredAccessibility == Accessibility.Public)
                {
                    publicCtors.Add(ctor);
                }
            }
            bool hasPublicConstructor = publicCtors.Count > 0;

            IMethodSymbol? chosenCtor = null;
            bool hasMultipleConstructors = false;
            var constructorParameters = new List<ConstructorParameterInfo>();
            string? primitiveParameterName = null;
            string? primitiveParameterType = null;
            string? optionalNonNullableParamName = null;
            string? optionalNonNullableParamType = null;
            LocationInfo? primitiveParameterLocation = null;
            LocationInfo? optionalNonNullableParamLocation = null;

            if (publicCtors.Count == 1)
            {
                chosenCtor = publicCtors[0];
            }
            else if (publicCtors.Count > 1)
            {
                // Look for [ActivatorUtilitiesConstructor]
                IMethodSymbol? attributedCtor = null;
                foreach (var ctor in publicCtors)
                {
                    foreach (var ctorAttr in ctor.GetAttributes())
                    {
                        if (ctorAttr.AttributeClass != null &&
                            ctorAttr.AttributeClass.Name == "ActivatorUtilitiesConstructorAttribute")
                        {
                            attributedCtor = ctor;
                            break;
                        }
                    }
                    if (attributedCtor != null) break;
                }

                if (attributedCtor != null)
                {
                    chosenCtor = attributedCtor;
                }
                else
                {
                    hasMultipleConstructors = true;
                }
            }

            if (chosenCtor != null)
            {
                foreach (var param in chosenCtor.Parameters)
                {
                    var paramTypeFqn = param.Type.ToDisplayString(FullyQualifiedFormat);
                    var paramAttrs = param.GetAttributes();
                    bool hasOptionalAttr = !param.HasExplicitDefaultValue
                        && paramAttrs.Any(a => a.AttributeClass?.ToDisplayString() == "ZeroAlloc.Inject.OptionalDependencyAttribute");
                    bool isOptional = param.HasExplicitDefaultValue || hasOptionalAttr;

                    // Check if [OptionalDependency] is used on a non-nullable reference type
                    // Only fire in nullable-enabled contexts (NotAnnotated), not when nullable is disabled (None)
                    if (hasOptionalAttr
                        && param.Type.NullableAnnotation == Microsoft.CodeAnalysis.NullableAnnotation.NotAnnotated
                        && optionalNonNullableParamName == null)
                    {
                        optionalNonNullableParamName = param.Name;
                        optionalNonNullableParamType = paramTypeFqn;
                        optionalNonNullableParamLocation = LocationInfo.From(param.Locations.FirstOrDefault());
                    }

                    string? unboundFqn = null;
                    ImmutableArray<string> typeArgReferenceIds = ImmutableArray<string>.Empty;
                    if (param.Type is INamedTypeSymbol namedParam
                        && namedParam.IsGenericType
                        && !namedParam.IsUnboundGenericType)
                    {
                        // Use ToUnboundGenericString so the format matches ServiceRegistrationInfo.Interfaces,
                        // which also stores open-generic interfaces in the "global::Ns.IFoo<,>" form (via
                        // ToUnboundGenericString at line ~421). Both sides must agree on the same format so
                        // that FindClosedGenericUsages can look up svc.Interfaces by UnboundGenericInterfaceFqn.
                        var rawFqn = namedParam.ConstructedFrom.ToDisplayString(FullyQualifiedFormat);
                        unboundFqn = ToUnboundGenericString(rawFqn, namedParam.TypeArguments.Length);
                        typeArgReferenceIds = ToTypeArgumentReferenceIds(namedParam);
                    }

                    constructorParameters.Add(new ConstructorParameterInfo(
                        paramTypeFqn,
                        param.Name,
                        isOptional,
                        unboundFqn,
                        typeArgReferenceIds));

                    // Check for primitive/value types
                    if (primitiveParameterName == null)
                    {
                        if (param.Type.IsValueType ||
                            paramTypeFqn == "global::System.String" ||
                            paramTypeFqn == "global::System.Uri" ||
                            paramTypeFqn == "global::System.Threading.CancellationToken" ||
                            paramTypeFqn == "string")
                        {
                            primitiveParameterName = param.Name;
                            primitiveParameterType = paramTypeFqn;
                            primitiveParameterLocation = LocationInfo.From(param.Locations.FirstOrDefault());
                        }
                    }
                }
            }

            // Property injection scanning
            var propertyInjections = new List<PropertyInjectionInfo>();
            var nonSettableInjectPropNames = new List<NamedLocationInfo>();
            foreach (var member in typeSymbol.GetMembers())
            {
                if (member is not IPropertySymbol propSymbol) continue;
                var injectAttr = propSymbol.GetAttributes()
                    .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == "ZeroAlloc.Inject.InjectAttribute");
                if (injectAttr == null) continue;

                // Validate: must have a public setter (init-only setters are treated as get-only)
                bool hasPublicSetter = propSymbol.SetMethod != null
                    && propSymbol.SetMethod.DeclaredAccessibility == Accessibility.Public
                    && !propSymbol.SetMethod.IsInitOnly;
                if (!hasPublicSetter)
                {
                    nonSettableInjectPropNames.Add(new NamedLocationInfo(
                        propSymbol.Name, LocationInfo.From(propSymbol.Locations.FirstOrDefault())));
                    continue;
                }

                bool isRequired = true;
                foreach (var namedArg in injectAttr.NamedArguments)
                {
                    if (namedArg.Key == "Required" && namedArg.Value.Value is bool reqVal)
                    {
                        isRequired = reqVal;
                        break;
                    }
                }

                var propTypeFqn = propSymbol.Type.ToDisplayString(FullyQualifiedFormat);
                propertyInjections.Add(new PropertyInjectionInfo(propTypeFqn, propSymbol.Name, isRequired));
            }

            string? implementationMetadataName = null;
            if (isOpenGeneric)
            {
                var implNs = typeSymbol.ContainingNamespace is { IsGlobalNamespace: false } ns2
                    ? ns2.ToDisplayString()
                    : null;
                implementationMetadataName = implNs != null
                    ? implNs + "." + typeSymbol.MetadataName
                    : typeSymbol.MetadataName;
            }

            return new ServiceRegistrationInfo(
                ns,
                typeName,
                fullyQualifiedName,
                lifetime,
                interfaces,
                asType,
                key,
                allowMultiple,
                isOpenGeneric,
                openGenericArity,
                hasPublicConstructor,
                constructorParameters,
                hasMultipleConstructors,
                primitiveParameterName,
                primitiveParameterType,
                optionalNonNullableParamName,
                optionalNonNullableParamType,
                implementsDisposable,
                implementationMetadataName,
                propertyInjections: propertyInjections,
                nonSettableInjectProperties: nonSettableInjectPropNames,
                hasMultipleLifetimes: hasMultipleLifetimes,
                asTypeNotImplemented: asTypeNotImplemented,
                location: location,
                attributeLocation: attributeLocation,
                primitiveParameterLocation: primitiveParameterLocation,
                optionalNonNullableParamLocation: optionalNonNullableParamLocation);
        }

        /// <summary>
        /// The identifier of the class declaration that carries the attribute. For a partial class
        /// this is the part the attribute is on.
        /// </summary>
        private static LocationInfo? GetClassLocation(GeneratorAttributeSyntaxContext ctx, INamedTypeSymbol typeSymbol)
        {
            if (ctx.TargetNode is BaseTypeDeclarationSyntax declaration)
            {
                return LocationInfo.From(declaration.Identifier.GetLocation());
            }

            return LocationInfo.From(typeSymbol.Locations.FirstOrDefault());
        }

        private static string GenerateExtensionClass(
            List<ServiceRegistrationInfo> services,
            string assemblyName,
            string? methodNameOverride,
            System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<DecoratorRegistrationInfo>> decoratorsByInterface,
            string accessibilityKeyword,
            ImmutableArray<ClosedGenericFactoryInfo> closedGenericFactories)
        {
            string methodName;
            if (methodNameOverride != null)
            {
                methodName = methodNameOverride;
            }
            else
            {
                // Remove dots, dashes, underscores from assembly name
                var cleanName = new StringBuilder();
                foreach (var c in assemblyName)
                {
                    if (c != '.' && c != '-' && c != '_')
                    {
                        cleanName.Append(c);
                    }
                }
                methodName = "Add" + cleanName.ToString() + "Services";
            }

            // Derive class name from method name
            // e.g. "AddDomainServices" -> "DomainServicesServiceCollectionExtensions"
            string className;
            if (methodName.StartsWith("Add"))
            {
                className = methodName.Substring(3) + "ServiceCollectionExtensions";
            }
            else
            {
                className = methodName + "ServiceCollectionExtensions";
            }

            bool hasConditionalDecorators = false;
            foreach (var list in decoratorsByInterface.Values)
            {
                foreach (var dec in list)
                {
                    if (dec.WhenRegisteredFqn != null) { hasConditionalDecorators = true; break; }
                }
                if (hasConditionalDecorators) break;
            }

            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated />");
            // CS1591: the generated members are public but intentionally undocumented.
            // The `// <auto-generated />` marker above does NOT exempt a source-generator
            // tree from CS1591, and a consumer cannot pragma or annotate code it does not
            // own — so a consumer with GenerateDocumentationFile + WarningsAsErrors;CS1591
            // (a doc-coverage gate) would fail to build on our output. Disable 1591 in the
            // generated file itself, as the Razor generator does.
            sb.AppendLine("#pragma warning disable 1591");
            sb.AppendLine("#nullable enable");
            sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
            sb.AppendLine("using Microsoft.Extensions.DependencyInjection.Extensions;");
            if (hasConditionalDecorators)
                sb.AppendLine("using System.Linq;");
            sb.AppendLine();
            sb.AppendLine("namespace Microsoft.Extensions.DependencyInjection");
            sb.AppendLine("{");
            sb.AppendLine("    " + accessibilityKeyword + " static class " + className);
            sb.AppendLine("    {");
            sb.AppendLine("        " + accessibilityKeyword + " static IServiceCollection " + methodName + "(this IServiceCollection services)");
            sb.AppendLine("        {");

            foreach (var svc in services)
            {
                if (svc.IsOpenGeneric)
                    EmitOpenGenericRegistration(sb, svc, decoratorsByInterface, closedGenericFactories);
                else
                    EmitRegistration(sb, svc, decoratorsByInterface);
            }

            EmitValueTypeClosedGenericRegistrations(sb, closedGenericFactories, decoratorsByInterface);

            sb.AppendLine("            return services;");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            return sb.ToString();
        }

        /// <summary>
        /// Registers the closed forms of open generics that constructors ask for with a value-type
        /// argument, when dynamic code is not supported. Microsoft DI then refuses to close an open
        /// generic over a value type, as under NativeAOT, so without them resolving one throws.
        /// With dynamic code they are left out: Microsoft DI treats a closed registration as a second
        /// registration next to the open one, so IEnumerable&lt;T&gt; would return both. Each closed
        /// registration is the one Microsoft DI resolves from the open registrations of its service and
        /// key, and uses TryAdd, so a closed registration the application made first still wins, as it
        /// does over the open one. A decorated closed form is registered closed in any case.
        /// </summary>
        private static void EmitValueTypeClosedGenericRegistrations(
            StringBuilder sb,
            ImmutableArray<ClosedGenericFactoryInfo> closedGenericFactories,
            Dictionary<string, List<DecoratorRegistrationInfo>> decoratorsByInterface)
        {
            var registrations = closedGenericFactories
                .Where(cgf => cgf.IsResolved && cgf.HasValueTypeArgument && ClosedGenericDecorators(cgf, decoratorsByInterface) == null)
                .ToList();
            if (registrations.Count == 0) return;

            sb.AppendLine();
            sb.AppendLine("            // Microsoft DI cannot close an open generic over a value type without dynamic code, as");
            sb.AppendLine("            // under NativeAOT, so the closed forms constructors ask for are registered closed. Only");
            sb.AppendLine("            // then: next to the open registration, a closed one is a second IEnumerable<T> entry.");
            sb.AppendLine("            if (!global::System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported)");
            sb.AppendLine("            {");
            foreach (var cgf in registrations)
            {
                sb.AppendLine("                services.TryAdd(" + TypeDescriptor(cgf.Lifetime, cgf.Key, cgf.InterfaceFqn, cgf.ImplementationFqn) + ");");
            }
            sb.AppendLine("            }");
            sb.AppendLine();
        }

        /// <summary>A ServiceDescriptor of a service type to an implementation type, keyed when a key is given.</summary>
        private static string TypeDescriptor(string lifetime, string? key, string serviceType, string implementationType) => key == null
            ? "ServiceDescriptor." + lifetime + "(typeof(" + serviceType + "), typeof(" + implementationType + "))"
            : "ServiceDescriptor.Keyed" + lifetime + "(typeof(" + serviceType + "), \"" + EscapeKey(key) + "\", typeof(" + implementationType + "))";

        /// <summary>
        /// Registers an open generic service by its open types, keyed when it has a key. Microsoft DI
        /// cannot decorate an open generic, so a decorated service type is registered instead as the
        /// decorated closed forms constructors ask for, each with the same lifetime and order. An open
        /// registration next to them would add an undecorated entry to IEnumerable&lt;T&gt;, so other
        /// closed forms of a decorated service type do not resolve, as in the generated containers.
        /// </summary>
        private static void EmitOpenGenericRegistration(
            StringBuilder sb,
            ServiceRegistrationInfo svc,
            Dictionary<string, List<DecoratorRegistrationInfo>> decoratorsByInterface,
            ImmutableArray<ClosedGenericFactoryInfo> closedGenericFactories)
        {
            var addOrTryAdd = svc.AllowMultiple ? "Add" : "TryAdd";
            var serviceTypes = svc.AsType != null ? new List<string> { svc.AsType } : svc.Interfaces;
            int arity = int.Parse(svc.OpenGenericArity!, System.Globalization.CultureInfo.InvariantCulture);

            foreach (var serviceType in serviceTypes)
            {
                if (svc.Key == null && decoratorsByInterface.ContainsKey(serviceType))
                {
                    foreach (var cgf in closedGenericFactories)
                    {
                        if (cgf.Key != null
                            || !string.Equals(cgf.OpenServiceFqn, serviceType, StringComparison.Ordinal)
                            || !string.Equals(ToUnboundGenericString(cgf.ImplementationFqn, arity), svc.FullyQualifiedName, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        var instance = BuildClosedGenericInstanceExpr(closedGenericFactories, cgf, decoratorsByInterface, ServiceProviderResolve);
                        sb.AppendLine("            services." + addOrTryAdd + svc.Lifetime + "<" + cgf.InterfaceFqn + ">(sp => " + instance + ");");
                    }
                    continue;
                }

                sb.AppendLine("            services." + addOrTryAdd + "(" + TypeDescriptor(svc.Lifetime, svc.Key, serviceType, svc.FullyQualifiedName) + ");");
            }

            // Without As, the concrete type is a service too, and the inner a decorated closed form wraps.
            if (svc.AsType == null)
            {
                sb.AppendLine("            services." + addOrTryAdd + "(" + TypeDescriptor(svc.Lifetime, svc.Key, svc.FullyQualifiedName, svc.FullyQualifiedName) + ");");
            }
        }

        private static string BuildFactoryLambda(string implType, List<ConstructorParameterInfo> parameters, List<PropertyInjectionInfo> propertyInjections)
        {
            return BuildFactoryLambdaCore(implType, parameters, false, propertyInjections);
        }

        private static string BuildKeyedFactoryLambda(string implType, List<ConstructorParameterInfo> parameters, List<PropertyInjectionInfo> propertyInjections)
        {
            return BuildFactoryLambdaCore(implType, parameters, true, propertyInjections);
        }

        private static string BuildFactoryLambdaCore(string implType, List<ConstructorParameterInfo> parameters, bool keyed, List<PropertyInjectionInfo> propertyInjections)
        {
            var spPrefix = keyed ? "(sp, _)" : "sp";
            bool hasProps = propertyInjections.Count > 0;

            // Build the constructor call expression
            var ctorSb = new StringBuilder();
            ctorSb.Append("new ").Append(implType).Append("(");
            if (parameters.Count > 0)
            {
                ctorSb.Append("\n");
                for (int i = 0; i < parameters.Count; i++)
                {
                    var param = parameters[i];
                    var method = param.IsOptional ? "GetService" : "GetRequiredService";
                    ctorSb.Append("                sp.");
                    ctorSb.Append(method).Append("<").Append(param.FullyQualifiedTypeName).Append(">()");
                    if (i < parameters.Count - 1) ctorSb.Append(",");
                    ctorSb.Append("\n");
                }
                ctorSb.Append("            )");
            }
            else
            {
                ctorSb.Append(")");
            }

            if (!hasProps)
            {
                // Expression lambda — same shape as before
                return spPrefix + " => " + ctorSb;
            }

            // Block lambda
            var sb = new StringBuilder();
            sb.Append(spPrefix).Append(" =>\n            {\n");
            sb.Append("                var instance = ").Append(ctorSb).Append(";\n");
            foreach (var prop in propertyInjections)
            {
                var method = prop.IsRequired ? "GetRequiredService" : "GetService";
                sb.Append("                instance.").Append(prop.PropertyName)
                  .Append(" = sp.").Append(method)
                  .Append("<").Append(prop.FullyQualifiedTypeName).Append(">();\n");
            }
            sb.Append("                return instance;\n");
            sb.Append("            }");
            return sb.ToString();
        }

        /// <summary>
        /// Registers a non-generic service. A decorated service type is registered as its decorator
        /// chain around the concrete registration, or around a new instance when As leaves the
        /// concrete type unregistered. A keyed service is not decorated, as in the generated
        /// containers: its decorators would wrap an unkeyed inner that is not registered.
        /// </summary>
        private static void EmitRegistration(
            StringBuilder sb,
            ServiceRegistrationInfo svc,
            System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<DecoratorRegistrationInfo>> decoratorsByInterface)
        {
            var lifetime = svc.Lifetime;
            var fqn = svc.FullyQualifiedName;
            var useAdd = svc.AllowMultiple;
            var serviceTypes = svc.AsType != null ? new List<string> { svc.AsType } : svc.Interfaces;

            foreach (var serviceType in serviceTypes)
            {
                if (svc.Key == null && decoratorsByInterface.TryGetValue(serviceType, out var decorators) && decorators.Count > 0)
                {
                    EmitDecoratedRegistration(sb, svc, serviceType, decorators);
                }
                else
                {
                    EmitSingleRegistration(sb, lifetime, serviceType, fqn, svc.Key, useAdd, svc.ConstructorParameters, svc.PropertyInjections);
                }
            }

            // Without As, the concrete type is a service too, and the inner its decorators wrap.
            if (svc.AsType == null)
            {
                EmitConcreteRegistration(sb, lifetime, fqn, svc.Key, useAdd, svc.ConstructorParameters, svc.PropertyInjections);
            }
        }

        /// <summary>
        /// Registers a decorated service type as one factory that chains its decorators, innermost
        /// first. A decorator with WhenRegistered is applied only when its type is in the service
        /// collection when this method runs. Skipping it leaves the rest of the chain, and the
        /// service, registered, as the next lower decorator becomes the outermost.
        /// </summary>
        private static void EmitDecoratedRegistration(
            StringBuilder sb,
            ServiceRegistrationInfo svc,
            string serviceType,
            List<DecoratorRegistrationInfo> decorators)
        {
            bool newInner = svc.AsType != null;
            bool conditional = decorators.Exists(static d => d.WhenRegisteredFqn != null);
            var register = "services.Add" + svc.Lifetime + "<" + serviceType + ">";

            if (!conditional && (!newInner || svc.PropertyInjections.Count == 0))
            {
                var chain = newInner
                    ? NewWithServiceProvider(svc)
                    : ServiceProviderResolve(svc.FullyQualifiedName, optional: false);
                foreach (var decorator in decorators)
                {
                    chain = NewDecorator(decorator, chain, ServiceProviderResolve);
                }
                sb.AppendLine("            " + register + "(sp => " + chain + ");");
                return;
            }

            sb.AppendLine("            {");
            for (int i = 0; i < decorators.Count; i++)
            {
                if (decorators[i].WhenRegisteredFqn is { } whenRegistered)
                    sb.AppendLine("                bool when" + i + " = services.Any(d => d.ServiceType == typeof(" + whenRegistered + "));");
            }
            sb.AppendLine("                " + register + "(sp =>");
            sb.AppendLine("                {");
            if (newInner)
            {
                sb.AppendLine("                    var instance = " + NewWithServiceProvider(svc) + ";");
                foreach (var prop in svc.PropertyInjections)
                {
                    sb.AppendLine("                    instance." + prop.PropertyName + " = " + ServiceProviderResolve(prop.FullyQualifiedTypeName, !prop.IsRequired) + ";");
                }
                sb.AppendLine("                    " + serviceType + " decorated = instance;");
            }
            else
            {
                sb.AppendLine("                    " + serviceType + " decorated = " + ServiceProviderResolve(svc.FullyQualifiedName, optional: false) + ";");
            }
            for (int i = 0; i < decorators.Count; i++)
            {
                var apply = "decorated = " + NewDecorator(decorators[i], "decorated", ServiceProviderResolve) + ";";
                sb.AppendLine("                    " + (decorators[i].WhenRegisteredFqn != null ? "if (when" + i + ") " + apply : apply));
            }
            sb.AppendLine("                    return decorated;");
            sb.AppendLine("                });");
            sb.AppendLine("            }");
        }

        /// <summary>A new instance of a service, its constructor parameters resolved from <c>sp</c>.</summary>
        private static string NewWithServiceProvider(ServiceRegistrationInfo svc)
        {
            var sb = new StringBuilder();
            sb.Append("new ").Append(svc.FullyQualifiedName).Append("(");
            for (int i = 0; i < svc.ConstructorParameters.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                var param = svc.ConstructorParameters[i];
                sb.Append(ServiceProviderResolve(param.FullyQualifiedTypeName, param.IsOptional));
            }
            sb.Append(")");
            return sb.ToString();
        }

        private static void EmitSingleRegistration(
            StringBuilder sb,
            string lifetime,
            string serviceType,
            string implType,
            string? key,
            bool useAdd,
            List<ConstructorParameterInfo> constructorParameters,
            List<PropertyInjectionInfo> propertyInjections)
        {
            if (key != null)
            {
                var method = useAdd ? "AddKeyed" + lifetime : "TryAddKeyed" + lifetime;
                var factory = BuildKeyedFactoryLambda(implType, constructorParameters, propertyInjections);
                var escapedKey = key.Replace("\\", "\\\\").Replace("\"", "\\\"");
                sb.AppendLine(string.Format(
                    "            services.{0}<{1}>(\"{2}\", {3});",
                    method, serviceType, escapedKey, factory));
            }
            else
            {
                var method = useAdd ? "Add" + lifetime : "TryAdd" + lifetime;
                var factory = BuildFactoryLambda(implType, constructorParameters, propertyInjections);
                sb.AppendLine(string.Format(
                    "            services.{0}<{1}>({2});",
                    method, serviceType, factory));
            }
        }

        /// <summary>
        /// The decorated service types a generated container resolves, one per unkeyed non-generic
        /// service and service type that has decorators, numbered for the members emitted for each.
        /// </summary>
        private static List<DecoratedServiceEntry> CollectDecoratedEntries(
            List<ServiceRegistrationInfo> services,
            Dictionary<string, List<DecoratorRegistrationInfo>> decoratorsByInterface)
        {
            var entries = new List<DecoratedServiceEntry>();
            foreach (var svc in services)
            {
                if (svc.IsOpenGeneric || svc.Key != null) continue;
                foreach (var serviceType in GetServiceTypes(svc))
                {
                    if (decoratorsByInterface.TryGetValue(serviceType, out var decorators) && decorators.Count > 0)
                        entries.Add(new DecoratedServiceEntry(svc, serviceType, decorators, entries.Count));
                }
            }
            return entries;
        }

        private static DecoratedServiceEntry? FindDecorated(
            List<DecoratedServiceEntry> entries,
            ServiceRegistrationInfo svc,
            string serviceType)
        {
            foreach (var entry in entries)
            {
                if (ReferenceEquals(entry.Svc, svc) && string.Equals(entry.ServiceType, serviceType, StringComparison.Ordinal))
                    return entry;
            }
            return null;
        }

        /// <summary>
        /// Whether a service has a service type without decorators, which a generated container
        /// caches the service's own instance for. A service registered with As whose one service
        /// type is decorated has none: its decorated factory creates the inner itself.
        /// </summary>
        private static bool HasUndecoratedServiceType(List<DecoratedServiceEntry> decoratedEntries, ServiceRegistrationInfo svc)
        {
            foreach (var serviceType in GetServiceTypes(svc))
            {
                if (FindDecorated(decoratedEntries, svc, serviceType) == null)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// The instance of a decorated entry in a generated root, with a null <paramref name="className"/>,
        /// or in its scope: a singleton comes from the root's cache, a scoped one from the scope's, and
        /// a transient is created and tracked for disposal when its outermost decorator is disposable.
        /// The inner is tracked by its own registration, as Microsoft DI tracks it.
        /// </summary>
        private static string DecoratedExpr(DecoratedServiceEntry entry, string? className)
        {
            var i = entry.Index;
            if (string.Equals(entry.Svc.Lifetime, "Singleton", StringComparison.Ordinal))
            {
                return className == null
                    ? "DecoratedSingleton" + i + "()"
                    : "((" + className + ")Root).DecoratedSingleton" + i + "()";
            }

            var created = TrackIfDisposable("NewDecorated" + i + "()", entry.Decorators[entry.Decorators.Count - 1].ImplementsDisposable);
            return string.Equals(entry.Svc.Lifetime, "Scoped", StringComparison.Ordinal)
                ? "(_decorated_" + i + " ??= " + created + ")"
                : created;
        }

        /// <summary>
        /// One entry of a generated root's cached IEnumerable&lt;T&gt; of singletons: a decorated one
        /// from its cache, any other through its concrete type.
        /// </summary>
        private static string EnumerableEntryExpr(
            List<DecoratedServiceEntry> decoratedEntries,
            ServiceTypeGroupEntry entry,
            string serviceType,
            string? className)
        {
            var decorated = FindDecorated(decoratedEntries, entry.Svc, serviceType);
            return decorated != null
                ? DecoratedExpr(decorated, className)
                : "(" + serviceType + ")GetService(typeof(" + entry.Svc.FullyQualifiedName + "))!";
        }

        /// <summary>
        /// The members a generated root, or with <paramref name="inScope"/> its scope, needs for its
        /// decorated entries: a factory for each entry it creates, and the cache of each singleton in
        /// the root and of each scoped entry in the scope.
        /// </summary>
        private static void EmitDecoratedMembers(StringBuilder sb, List<DecoratedServiceEntry> entries, bool inScope)
        {
            var indent = inScope ? "            " : "        ";
            foreach (var entry in entries)
            {
                var lifetime = entry.Svc.Lifetime;
                bool singleton = string.Equals(lifetime, "Singleton", StringComparison.Ordinal);
                bool scoped = string.Equals(lifetime, "Scoped", StringComparison.Ordinal);
                if (inScope ? singleton : scoped) continue;

                var i = entry.Index;
                if (singleton || scoped)
                    sb.AppendLine(indent + "private " + entry.ServiceType + "? _decorated_" + i + ";");

                EmitDecoratedFactory(sb, entry, indent);

                if (singleton)
                {
                    var field = "_decorated_" + i;
                    sb.AppendLine(indent + "private " + entry.ServiceType + " DecoratedSingleton" + i + "()");
                    sb.AppendLine(indent + "{");
                    sb.AppendLine(indent + "    var existing = " + field + ";");
                    sb.AppendLine(indent + "    if (existing != null) return existing;");
                    sb.AppendLine(indent + "    var instance = NewDecorated" + i + "();");
                    sb.AppendLine(indent + "    var winner = Interlocked.CompareExchange(ref " + field + ", instance, null);");
                    // The instance that loses a race is not disposed: its inner is the shared concrete
                    // singleton, which a decorator may dispose with itself.
                    sb.AppendLine(entry.Decorators[entry.Decorators.Count - 1].ImplementsDisposable
                        ? indent + "    if (winner == null) return TrackDisposable(instance);"
                        : indent + "    if (winner == null) return instance;");
                    sb.AppendLine(indent + "    return winner;");
                    sb.AppendLine(indent + "}");
                    sb.AppendLine();
                }
            }
        }

        /// <summary>
        /// A factory that chains an entry's decorators, innermost first, around its inner: the
        /// concrete registration of the service, as the Add...Services extension resolves it, so the
        /// inner has that registration's lifetime and is disposed with it. When As leaves the concrete
        /// type unregistered, the decorators wrap a new instance, as in the extension.
        /// </summary>
        private static void EmitDecoratedFactory(StringBuilder sb, DecoratedServiceEntry entry, string indent)
        {
            var svc = entry.Svc;
            sb.AppendLine(indent + "private " + entry.ServiceType + " NewDecorated" + entry.Index + "()");
            sb.AppendLine(indent + "{");
            if (svc.AsType == null)
            {
                sb.AppendLine(indent + "    " + entry.ServiceType + " inner = " + ContainerResolve(svc.FullyQualifiedName, optional: false) + ";");
            }
            else
            {
                sb.AppendLine(indent + "    var instance = " + BuildNewExpression(svc) + ";");
                AppendPropertySetters(sb, svc.PropertyInjections, indent + "    ");
                sb.AppendLine(indent + "    " + entry.ServiceType + " inner = instance;");
            }
            var chain = "inner";
            foreach (var decorator in entry.Decorators)
            {
                chain = NewDecorator(decorator, chain, ContainerResolve);
            }
            sb.AppendLine(indent + "    return " + chain + ";");
            sb.AppendLine(indent + "}");
            sb.AppendLine();
        }

        /// <summary>
        /// A new decorator around <paramref name="innerExpr"/>: its parameter of the decorated type
        /// takes the inner, and its other parameters are resolved.
        /// </summary>
        private static string NewDecorator(
            DecoratorRegistrationInfo decorator,
            string innerExpr,
            Func<string, bool, string> resolve)
        {
            var sb = new StringBuilder();
            sb.Append("new ").Append(decorator.DecoratorFqn).Append("(");
            bool first = true;
            foreach (var param in decorator.ConstructorParameters)
            {
                if (!first) sb.Append(", ");
                first = false;
                if (string.Equals(param.FullyQualifiedTypeName, decorator.DecoratedInterfaceFqn, StringComparison.Ordinal))
                    sb.Append(innerExpr);
                else
                    sb.Append(resolve(param.FullyQualifiedTypeName, param.IsOptional));
            }
            sb.Append(")");
            return sb.ToString();
        }

        private static string GenerateServiceProviderClass(
            List<ServiceRegistrationInfo> services,
            string assemblyName,
            System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<DecoratorRegistrationInfo>> decoratorsByInterface,
            string accessibilityKeyword,
            ImmutableArray<ClosedGenericFactoryInfo> closedGenericFactories)
        {
            // Clean assembly name for class naming
            var cleanName = new StringBuilder();
            foreach (var c in assemblyName)
            {
                if (c != '.' && c != '-' && c != '_')
                {
                    cleanName.Append(c);
                }
            }
            var className = cleanName.ToString() + "ServiceProvider";

            // Separate services by lifetime (skip open generics - can't be resolved statically)
            var transients = new List<ServiceRegistrationInfo>();
            var singletons = new List<ServiceRegistrationInfo>();
            var scopeds = new List<ServiceRegistrationInfo>();
            var keyedServices = new List<ServiceRegistrationInfo>();

            foreach (var svc in services)
            {
                if (svc.IsOpenGeneric) continue;
                if (svc.Key != null)
                {
                    keyedServices.Add(svc);
                    continue;
                }
                if (svc.Lifetime == "Transient") transients.Add(svc);
                else if (svc.Lifetime == "Singleton") singletons.Add(svc);
                else if (svc.Lifetime == "Scoped") scopeds.Add(svc);
            }

            // Group non-keyed services by service type for IEnumerable<T> support.
            // Each entry maps a service type to the list of (service, lifetime, fieldIndex).
            // fieldIndex is the index in the corresponding lifetime list (for singleton/scoped field references).
            var serviceTypeGroups = new Dictionary<string, List<ServiceTypeGroupEntry>>();

            for (int i = 0; i < transients.Count; i++)
            {
                var svc = transients[i];
                foreach (var st in GetServiceTypes(svc))
                {
                    if (!serviceTypeGroups.ContainsKey(st))
                        serviceTypeGroups[st] = new List<ServiceTypeGroupEntry>();
                    serviceTypeGroups[st].Add(new ServiceTypeGroupEntry(svc, "Transient", i));
                }
            }
            for (int i = 0; i < singletons.Count; i++)
            {
                var svc = singletons[i];
                foreach (var st in GetServiceTypes(svc))
                {
                    if (!serviceTypeGroups.ContainsKey(st))
                        serviceTypeGroups[st] = new List<ServiceTypeGroupEntry>();
                    serviceTypeGroups[st].Add(new ServiceTypeGroupEntry(svc, "Singleton", i));
                }
            }
            for (int i = 0; i < scopeds.Count; i++)
            {
                var svc = scopeds[i];
                foreach (var st in GetServiceTypes(svc))
                {
                    if (!serviceTypeGroups.ContainsKey(st))
                        serviceTypeGroups[st] = new List<ServiceTypeGroupEntry>();
                    serviceTypeGroups[st].Add(new ServiceTypeGroupEntry(svc, "Scoped", i));
                }
            }

            // Determine which entry is the last registration per service type (for last-wins behavior)
            var lastRegistrationPerType = new Dictionary<string, ServiceTypeGroupEntry>();
            foreach (var kvp in serviceTypeGroups)
            {
                lastRegistrationPerType[kvp.Key] = kvp.Value[kvp.Value.Count - 1];
            }

            // Pre-compute IEnumerable<T> cache field map for all-singleton groups (root level).
            // rootEntries excludes Scoped lifetime to match the root-level emit logic below.
            var enumerableCacheFields = new List<(string FieldName, string ServiceType)>();
            var cacheFieldByServiceType = new Dictionary<string, string>(StringComparer.Ordinal);
            {
                int counter = 0;
                foreach (var kvp in serviceTypeGroups)
                {
                    var rootEntriesForCache = new List<ServiceTypeGroupEntry>();
                    foreach (var entry in kvp.Value)
                        if (entry.Lifetime != "Scoped") rootEntriesForCache.Add(entry);
                    if (IsAllSingleton(rootEntriesForCache))
                    {
                        var fieldName = "_enumerable_" + SanitizeForFieldName(kvp.Key) + "_" + counter++;
                        enumerableCacheFields.Add((fieldName, kvp.Key));
                        cacheFieldByServiceType[kvp.Key] = fieldName;
                    }
                }
            }

            bool hasKeyedServices = keyedServices.Count > 0 || closedGenericFactories.Any(static cgf => cgf.Key != null);
            var decoratedEntries = CollectDecoratedEntries(services, decoratorsByInterface);

            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated />");
            // CS1591: the generated members are public but intentionally undocumented.
            // The `// <auto-generated />` marker above does NOT exempt a source-generator
            // tree from CS1591, and a consumer cannot pragma or annotate code it does not
            // own — so a consumer with GenerateDocumentationFile + WarningsAsErrors;CS1591
            // (a doc-coverage gate) would fail to build on our output. Disable 1591 in the
            // generated file itself, as the Razor generator does.
            sb.AppendLine("#pragma warning disable 1591");
            sb.AppendLine("#nullable enable");
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Threading;");
            sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
            sb.AppendLine();
            sb.AppendLine("namespace ZeroAlloc.Inject.Generated");
            sb.AppendLine("{");
            var baseClass = "global::ZeroAlloc.Inject.Container.ZeroAllocInjectServiceProviderBase";
            sb.AppendLine("    internal sealed class " + className + " : " + baseClass);
            sb.AppendLine("    {");

            // Separate keyed services by lifetime
            var keyedSingletons = new List<ServiceRegistrationInfo>();
            var keyedTransients = new List<ServiceRegistrationInfo>();
            var keyedScopedServices = new List<ServiceRegistrationInfo>();

            foreach (var svc in keyedServices)
            {
                if (svc.Lifetime == "Singleton") keyedSingletons.Add(svc);
                else if (svc.Lifetime == "Transient") keyedTransients.Add(svc);
                else if (svc.Lifetime == "Scoped") keyedScopedServices.Add(svc);
            }

            // Singleton fields
            for (int i = 0; i < singletons.Count; i++)
            {
                if (HasUndecoratedServiceType(decoratedEntries, singletons[i]))
                    sb.AppendLine("        private " + singletons[i].FullyQualifiedName + "? _singleton_" + i + ";");
            }
            // Keyed singleton fields
            for (int i = 0; i < keyedSingletons.Count; i++)
            {
                sb.AppendLine("        private " + keyedSingletons[i].FullyQualifiedName + "? _keyedSingleton_" + i + ";");
            }
            EmitClosedGenericSingletonFields(sb, closedGenericFactories);
            // IEnumerable<T> cache fields (one per all-singleton enumerable group)
            foreach (var (fieldName, fieldServiceType) in enumerableCacheFields)
            {
                sb.AppendLine("        private " + fieldServiceType + "[]? " + fieldName + ";");
            }
            if (singletons.Count > 0 || keyedSingletons.Count > 0 || closedGenericFactories.Length > 0 || enumerableCacheFields.Count > 0)
            {
                sb.AppendLine();
            }

            // Constructor
            sb.AppendLine("        public " + className + "(IServiceCollection fallbackServices) : base(fallbackServices) { }");
            sb.AppendLine();

            // ResolveKnown - root provider: transients + singletons (scoped returns null)
            sb.AppendLine("        protected override object? ResolveKnown(Type serviceType)");
            sb.AppendLine("        {");

            // Transients
            foreach (var svc in transients)
            {
                var serviceTypes = GetServiceTypes(svc);
                foreach (var serviceType in serviceTypes)
                {
                    ServiceTypeGroupEntry lastEntry;
                    if (lastRegistrationPerType.TryGetValue(serviceType, out lastEntry)
                        && lastEntry.Svc == svc && lastEntry.Lifetime == "Transient")
                    {
                        sb.AppendLine("            if (serviceType == typeof(" + serviceType + "))");
                        if (FindDecorated(decoratedEntries, svc, serviceType) is { } decoratedTransient)
                        {
                            sb.AppendLine("                return " + DecoratedExpr(decoratedTransient, className: null) + ";");
                            continue;
                        }
                        var newExpr = BuildNewExpression(svc);
                        if (svc.PropertyInjections.Count > 0)
                        {
                            sb.AppendLine("            {");
                            sb.AppendLine("                var instance = " + newExpr + ";");
                            AppendPropertySetters(sb, svc.PropertyInjections, "                ");
                            sb.AppendLine(svc.ImplementsDisposable
                                ? "                return TrackDisposable(instance);"
                                : "                return instance;");
                            sb.AppendLine("            }");
                        }
                        else
                        {
                            sb.AppendLine("                return " + TrackIfDisposable(newExpr, svc.ImplementsDisposable) + ";");
                        }
                    }
                }
            }

            // Singletons
            for (int i = 0; i < singletons.Count; i++)
            {
                var svc = singletons[i];
                var fieldName = "_singleton_" + i;
                var newExpr = BuildNewExpression(svc);
                var serviceTypes = GetServiceTypes(svc);

                foreach (var serviceType in serviceTypes)
                {
                    ServiceTypeGroupEntry lastEntry;
                    if (!lastRegistrationPerType.TryGetValue(serviceType, out lastEntry)
                        || lastEntry.Svc != svc || lastEntry.Lifetime != "Singleton")
                    {
                        continue; // Not the last registration for this service type
                    }

                    sb.AppendLine("            if (serviceType == typeof(" + serviceType + "))");
                    if (FindDecorated(decoratedEntries, svc, serviceType) is { } decoratedSingleton)
                    {
                        sb.AppendLine("                return " + DecoratedExpr(decoratedSingleton, className: null) + ";");
                        continue;
                    }
                    sb.AppendLine("            {");
                    sb.AppendLine("                if (" + fieldName + " != null) return " + fieldName + ";");
                    sb.AppendLine("                var instance = " + newExpr + ";");
                    AppendPropertySetters(sb, svc.PropertyInjections, "                ");
                    if (svc.ImplementsDisposable)
                    {
                        sb.AppendLine("                var existing = Interlocked.CompareExchange(ref " + fieldName + ", instance, null);");
                        sb.AppendLine("                if (existing != null) { (instance as System.IDisposable)?.Dispose(); return existing; }");
                        sb.AppendLine("                return TrackDisposable(instance);");
                    }
                    else
                    {
                        sb.AppendLine("                return Interlocked.CompareExchange(ref " + fieldName + ", instance, null) ?? " + fieldName + ";");
                    }
                    sb.AppendLine("            }");
                }
            }

            // IEnumerable<T> resolution
            foreach (var kvp in serviceTypeGroups)
            {
                var serviceType = kvp.Key;
                var entries = kvp.Value;

                // Root excludes scoped services
                var rootEntries = new List<ServiceTypeGroupEntry>();
                foreach (var entry in entries)
                {
                    if (entry.Lifetime != "Scoped")
                        rootEntries.Add(entry);
                }
                if (rootEntries.Count == 0) continue;

                sb.AppendLine("            if (serviceType == typeof(System.Collections.Generic.IEnumerable<" + serviceType + ">))");
                sb.AppendLine("            {");
                if (cacheFieldByServiceType.TryGetValue(serviceType, out var cacheFieldName))
                {
                    sb.Append("                return " + cacheFieldName + " ??= new " + serviceType + "[] { ");
                    for (int j = 0; j < rootEntries.Count; j++)
                    {
                        if (j > 0) sb.Append(", ");
                        sb.Append(EnumerableEntryExpr(decoratedEntries, rootEntries[j], serviceType, className: null));
                    }
                    sb.AppendLine(" };");
                }
                else
                {
                    sb.Append("                return new " + serviceType + "[] { ");

                    for (int j = 0; j < rootEntries.Count; j++)
                    {
                        if (j > 0) sb.Append(", ");
                        var entry = rootEntries[j];

                        if (FindDecorated(decoratedEntries, entry.Svc, serviceType) is { } decoratedEntry)
                        {
                            sb.Append(DecoratedExpr(decoratedEntry, className: null));
                        }
                        else if (entry.Lifetime == "Transient")
                        {
                            sb.Append(TrackIfDisposable(BuildNewExpression(entry.Svc), entry.Svc.ImplementsDisposable));
                        }
                        else if (entry.Lifetime == "Singleton")
                        {
                            // Use concrete type to resolve — avoids last-wins returning same instance for all
                            sb.Append("(" + serviceType + ")GetService(typeof(" + entry.Svc.FullyQualifiedName + "))!");
                        }
                    }

                    sb.AppendLine(" };");
                }
                sb.AppendLine("            }");
            }

            EmitClosedGenericRoot(sb, closedGenericFactories, decoratorsByInterface);

            sb.AppendLine("            return null;");
            sb.AppendLine("        }");
            sb.AppendLine();

            EmitClosedGenericSingletonAccessors(sb, closedGenericFactories, decoratorsByInterface);
            EmitDecoratedMembers(sb, decoratedEntries, inScope: false);

            EmitIsKnownService(sb, serviceTypeGroups, closedGenericFactories);
            sb.AppendLine();

            EmitIsKnownKeyedService(sb, keyedServices, closedGenericFactories);
            sb.AppendLine();

            // Keyed services the generator knows; the base resolves any other key, and a null one.
            if (hasKeyedServices)
            {
                sb.AppendLine("        protected override object? ResolveKnownKeyed(Type serviceType, object serviceKey)");
                sb.AppendLine("        {");
                sb.AppendLine("            if (serviceKey is string key)");
                sb.AppendLine("            {");

                // Keyed singletons - cached with Interlocked.CompareExchange
                for (int i = 0; i < keyedSingletons.Count; i++)
                {
                    var svc = keyedSingletons[i];
                    var serviceTypes = GetServiceTypes(svc);
                    var newExpr = BuildNewExpression(svc);
                    var fieldName = "_keyedSingleton_" + i;
                    var escapedKey = svc.Key!.Replace("\\", "\\\\").Replace("\"", "\\\"");
                    foreach (var serviceType in serviceTypes)
                    {
                        sb.AppendLine("                if (serviceType == typeof(" + serviceType + ") && key == \"" + escapedKey + "\")");
                        sb.AppendLine("                {");
                        sb.AppendLine("                    if (" + fieldName + " != null) return " + fieldName + ";");
                        sb.AppendLine("                    var instance = " + newExpr + ";");
                        if (svc.PropertyInjections.Count > 0)
                        {
                            AppendPropertySetters(sb, svc.PropertyInjections, "                    ");
                        }
                        if (svc.ImplementsDisposable)
                        {
                            sb.AppendLine("                    var existing = Interlocked.CompareExchange(ref " + fieldName + ", instance, null);");
                            sb.AppendLine("                    if (existing != null) { (instance as System.IDisposable)?.Dispose(); return existing; }");
                            sb.AppendLine("                    return TrackDisposable(instance);");
                        }
                        else
                        {
                            sb.AppendLine("                    return Interlocked.CompareExchange(ref " + fieldName + ", instance, null) ?? " + fieldName + ";");
                        }
                        sb.AppendLine("                }");
                    }
                }

                // Keyed transients - new instance each call, tracked when disposable
                foreach (var svc in keyedTransients)
                {
                    var serviceTypes = GetServiceTypes(svc);
                    var newExpr = BuildNewExpression(svc);
                    var escapedKey = svc.Key!.Replace("\\", "\\\\").Replace("\"", "\\\"");
                    foreach (var serviceType in serviceTypes)
                    {
                        sb.AppendLine("                if (serviceType == typeof(" + serviceType + ") && key == \"" + escapedKey + "\")");
                        if (svc.PropertyInjections.Count > 0)
                        {
                            sb.AppendLine("                {");
                            sb.AppendLine("                    var instance = " + newExpr + ";");
                            AppendPropertySetters(sb, svc.PropertyInjections, "                    ");
                            sb.AppendLine(svc.ImplementsDisposable
                                ? "                    return TrackDisposable(instance);"
                                : "                    return instance;");
                            sb.AppendLine("                }");
                        }
                        else
                        {
                            sb.AppendLine("                    return " + TrackIfDisposable(newExpr, svc.ImplementsDisposable) + ";");
                        }
                    }
                }

                EmitKeyedClosedGenericBranches(sb, closedGenericFactories, decoratorsByInterface, className: null, "                ");

                sb.AppendLine("            }");
                sb.AppendLine("            return null;");
                sb.AppendLine("        }");
                sb.AppendLine();
            }

            // CreateScopeCore
            sb.AppendLine("        protected override global::ZeroAlloc.Inject.Container.ZeroAllocInjectScope CreateScopeCore(global::Microsoft.Extensions.DependencyInjection.IServiceScopeFactory fallbackScopeFactory)");
            sb.AppendLine("        {");
            sb.AppendLine("            return new Scope(this, fallbackScopeFactory);");
            sb.AppendLine("        }");
            sb.AppendLine();

            // Nested Scope class
            var scopeBase = "global::ZeroAlloc.Inject.Container.ZeroAllocInjectScope";
            sb.AppendLine("        private sealed class Scope : " + scopeBase);
            sb.AppendLine("        {");

            // Scoped fields
            for (int i = 0; i < scopeds.Count; i++)
            {
                if (HasUndecoratedServiceType(decoratedEntries, scopeds[i]))
                    sb.AppendLine("            private " + scopeds[i].FullyQualifiedName + "? _scoped_" + i + ";");
            }
            for (int i = 0; i < keyedScopedServices.Count; i++)
            {
                sb.AppendLine("            private " + keyedScopedServices[i].FullyQualifiedName + "? _keyedScoped_" + i + ";");
            }
            bool hasScopedClosedGenerics = EmitClosedGenericScopedFields(sb, closedGenericFactories);
            if (scopeds.Count > 0 || keyedScopedServices.Count > 0 || hasScopedClosedGenerics)
            {
                sb.AppendLine();
            }

            // Scope constructor
            sb.AppendLine("            public Scope(" + className + " root, global::Microsoft.Extensions.DependencyInjection.IServiceScopeFactory fallbackScopeFactory) : base(root, fallbackScopeFactory) { }");
            sb.AppendLine();
            EmitDecoratedMembers(sb, decoratedEntries, inScope: true);

            // ResolveScopedKnown
            sb.AppendLine("            protected override object? ResolveScopedKnown(Type serviceType)");
            sb.AppendLine("            {");

            // Transients in scope - fresh instance each call
            foreach (var svc in transients)
            {
                var serviceTypes = GetServiceTypes(svc);
                foreach (var serviceType in serviceTypes)
                {
                    ServiceTypeGroupEntry lastEntry;
                    if (lastRegistrationPerType.TryGetValue(serviceType, out lastEntry)
                        && lastEntry.Svc == svc && lastEntry.Lifetime == "Transient")
                    {
                        sb.AppendLine("                if (serviceType == typeof(" + serviceType + "))");
                        if (FindDecorated(decoratedEntries, svc, serviceType) is { } decoratedTransient)
                        {
                            sb.AppendLine("                    return " + DecoratedExpr(decoratedTransient, className) + ";");
                            continue;
                        }
                        var newExpr = BuildNewExpressionForScope(svc);
                        if (svc.PropertyInjections.Count > 0)
                        {
                            sb.AppendLine("                {");
                            sb.AppendLine("                    var instance = " + newExpr + ";");
                            AppendPropertySetters(sb, svc.PropertyInjections, "                    ");
                            if (svc.ImplementsDisposable)
                            {
                                sb.AppendLine("                    return TrackDisposable(instance);");
                            }
                            else
                            {
                                sb.AppendLine("                    return instance;");
                            }
                            sb.AppendLine("                }");
                        }
                        else
                        {
                            if (svc.ImplementsDisposable)
                            {
                                newExpr = "TrackDisposable(" + newExpr + ")";
                            }
                            sb.AppendLine("                    return " + newExpr + ";");
                        }
                    }
                }
            }

            // Singletons in scope - delegate to Root
            foreach (var svc in singletons)
            {
                var serviceTypes = GetServiceTypes(svc);
                foreach (var serviceType in serviceTypes)
                {
                    ServiceTypeGroupEntry lastEntry;
                    if (!lastRegistrationPerType.TryGetValue(serviceType, out lastEntry)
                        || lastEntry.Svc != svc || lastEntry.Lifetime != "Singleton")
                    {
                        continue;
                    }
                    sb.AppendLine("                if (serviceType == typeof(" + serviceType + "))");
                    sb.AppendLine("                    return Root.GetService(serviceType);");
                }
            }

            // Scoped services
            for (int i = 0; i < scopeds.Count; i++)
            {
                var svc = scopeds[i];
                var fieldName = "_scoped_" + i;
                var innerExpr = BuildNewExpressionForScope(svc);
                var serviceTypes = GetServiceTypes(svc);

                foreach (var serviceType in serviceTypes)
                {
                    ServiceTypeGroupEntry lastEntry;
                    if (!lastRegistrationPerType.TryGetValue(serviceType, out lastEntry)
                        || lastEntry.Svc != svc || lastEntry.Lifetime != "Scoped")
                    {
                        continue;
                    }

                    sb.AppendLine("                if (serviceType == typeof(" + serviceType + "))");
                    sb.AppendLine("                {");
                    if (FindDecorated(decoratedEntries, svc, serviceType) is { } decoratedScoped)
                    {
                        sb.AppendLine("                    return " + DecoratedExpr(decoratedScoped, className) + ";");
                    }
                    else if (svc.ImplementsDisposable)
                    {
                        if (svc.PropertyInjections.Count > 0)
                        {
                            sb.AppendLine("                    if (" + fieldName + " == null) { " + fieldName + " = " + innerExpr + ";");
                            AppendPropertySetters(sb, svc.PropertyInjections, "                        ", fieldName);
                            sb.AppendLine("                        TrackDisposable(" + fieldName + "); }");
                        }
                        else
                        {
                            sb.AppendLine("                    if (" + fieldName + " == null) { " + fieldName + " = " + innerExpr + "; TrackDisposable(" + fieldName + "); }");
                        }
                        sb.AppendLine("                    return " + fieldName + ";");
                    }
                    else
                    {
                        if (svc.PropertyInjections.Count > 0)
                        {
                            sb.AppendLine("                    if (" + fieldName + " == null) { " + fieldName + " = " + innerExpr + ";");
                            AppendPropertySetters(sb, svc.PropertyInjections, "                        ", fieldName);
                            sb.AppendLine("                    }");
                        }
                        else
                        {
                            sb.AppendLine("                    if (" + fieldName + " == null) " + fieldName + " = " + innerExpr + ";");
                        }
                        sb.AppendLine("                    return " + fieldName + ";");
                    }
                    sb.AppendLine("                }");
                }
            }

            // IEnumerable<T> resolution in scope (all lifetimes)
            foreach (var kvp in serviceTypeGroups)
            {
                var serviceType = kvp.Key;
                var entries = kvp.Value;
                if (entries.Count == 0) continue;

                sb.AppendLine("                if (serviceType == typeof(System.Collections.Generic.IEnumerable<" + serviceType + ">))");
                sb.AppendLine("                {");

                if (IsAllSingleton(entries))
                {
                    // All entries are singletons — delegate to root provider's cached IEnumerable<T> path.
                    // No scope-level disposal tracking needed (singletons are tracked at provider scope).
                    sb.AppendLine("                    return Root.GetService(typeof(System.Collections.Generic.IEnumerable<" + serviceType + ">));");
                }
                else
                {
                    sb.Append("                    return new " + serviceType + "[] { ");

                    for (int j = 0; j < entries.Count; j++)
                    {
                        if (j > 0) sb.Append(", ");
                        var entry = entries[j];

                        if (FindDecorated(decoratedEntries, entry.Svc, serviceType) is { } decoratedEntry)
                        {
                            sb.Append(DecoratedExpr(decoratedEntry, className));
                        }
                        else if (entry.Lifetime == "Transient")
                        {
                            var newExpr = BuildNewExpressionForScope(entry.Svc);
                            if (entry.Svc.ImplementsDisposable)
                            {
                                sb.Append("TrackDisposable(" + newExpr + ")");
                            }
                            else
                            {
                                sb.Append(newExpr);
                            }
                        }
                        else if (entry.Lifetime == "Singleton")
                        {
                            // Use concrete type to resolve — avoids last-wins returning same instance for all
                            sb.Append("(" + serviceType + ")Root.GetService(typeof(" + entry.Svc.FullyQualifiedName + "))!");
                        }
                        else if (entry.Lifetime == "Scoped")
                        {
                            var fieldName = "_scoped_" + entry.FieldIndex;
                            var newExpr = BuildNewExpressionForScope(entry.Svc);
                            if (entry.Svc.ImplementsDisposable)
                            {
                                sb.Append(fieldName + " ?? (" + fieldName + " = TrackDisposable(" + newExpr + "))");
                            }
                            else
                            {
                                sb.Append(fieldName + " ?? (" + fieldName + " = " + newExpr + ")");
                            }
                        }
                    }

                    sb.AppendLine(" };");
                }
                sb.AppendLine("                }");
            }

            EmitClosedGenericScope(sb, closedGenericFactories, decoratorsByInterface, className);

            sb.AppendLine("                return null;");
            sb.AppendLine("            }");

            // Keyed services the generator knows, in scope; the base resolves any other key.
            if (hasKeyedServices)
            {
                sb.AppendLine();
                sb.AppendLine("            protected override object? ResolveScopedKnownKeyed(Type serviceType, object serviceKey)");
                sb.AppendLine("            {");
                sb.AppendLine("                if (serviceKey is string key)");
                sb.AppendLine("                {");

                // Keyed singletons - delegate to root
                foreach (var svc in keyedSingletons)
                {
                    var serviceTypes = GetServiceTypes(svc);
                    var escapedKey = svc.Key!.Replace("\\", "\\\\").Replace("\"", "\\\"");
                    foreach (var serviceType in serviceTypes)
                    {
                        sb.AppendLine("                    if (serviceType == typeof(" + serviceType + ") && key == \"" + escapedKey + "\")");
                        sb.AppendLine("                        return ((" + className + ")Root).ResolveKnownKeyed(serviceType, serviceKey);");
                    }
                }

                // Keyed scoped services - cached per scope
                for (int i = 0; i < keyedScopedServices.Count; i++)
                {
                    var svc = keyedScopedServices[i];
                    var serviceTypes = GetServiceTypes(svc);
                    var escapedKey = svc.Key!.Replace("\\", "\\\\").Replace("\"", "\\\"");
                    var fieldName = "_keyedScoped_" + i;
                    var newExpr = BuildNewExpressionForScope(svc);
                    foreach (var serviceType in serviceTypes)
                    {
                        sb.AppendLine("                    if (serviceType == typeof(" + serviceType + ") && key == \"" + escapedKey + "\")");
                        sb.AppendLine("                    {");
                        if (svc.ImplementsDisposable)
                        {
                            sb.AppendLine("                        if (" + fieldName + " == null) { " + fieldName + " = " + newExpr + "; TrackDisposable(" + fieldName + "); }");
                        }
                        else
                        {
                            sb.AppendLine("                        if (" + fieldName + " == null) " + fieldName + " = " + newExpr + ";");
                        }
                        sb.AppendLine("                        return " + fieldName + ";");
                        sb.AppendLine("                    }");
                    }
                }

                // Keyed transients - fresh instance, track disposable if needed
                foreach (var svc in keyedTransients)
                {
                    var serviceTypes = GetServiceTypes(svc);
                    var escapedKey = svc.Key!.Replace("\\", "\\\\").Replace("\"", "\\\"");
                    var newExpr = BuildNewExpressionForScope(svc);
                    if (svc.ImplementsDisposable)
                    {
                        newExpr = "TrackDisposable(" + newExpr + ")";
                    }
                    foreach (var serviceType in serviceTypes)
                    {
                        sb.AppendLine("                    if (serviceType == typeof(" + serviceType + ") && key == \"" + escapedKey + "\")");
                        sb.AppendLine("                        return " + newExpr + ";");
                    }
                }

                EmitKeyedClosedGenericBranches(sb, closedGenericFactories, decoratorsByInterface, className, "                    ");

                sb.AppendLine("                }");
                sb.AppendLine("                return null;");
                sb.AppendLine("            }");
            }

            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            sb.AppendLine();

            // Generate extension method and factory in Microsoft.Extensions.DependencyInjection namespace
            sb.AppendLine("namespace Microsoft.Extensions.DependencyInjection");
            sb.AppendLine("{");

            // BuildZeroAllocInjectServiceProvider extension method
            sb.AppendLine("    " + accessibilityKeyword + " static class ZeroAllocInjectServiceCollectionExtensions");
            sb.AppendLine("    {");
            sb.AppendLine("        " + accessibilityKeyword + " static IServiceProvider BuildZeroAllocInjectServiceProvider(this IServiceCollection services)");
            sb.AppendLine("        {");
            sb.AppendLine("            // Snapshot the collection so subsequent mutations don't leak into the lazily-built fallback.");
            sb.AppendLine("            IServiceCollection snapshot = new ServiceCollection();");
            sb.AppendLine("            foreach (var d in services) snapshot.Add(d);");
            sb.AppendLine("            return new global::ZeroAlloc.Inject.Generated." + className + "(snapshot);");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine();

            // ZeroAllocInjectServiceProviderFactory
            // Note: the interface members below stay `public` regardless of accessibilityKeyword — implicit
            // interface implementation requires a public member even when the containing type is internal.
            sb.AppendLine("    " + accessibilityKeyword + " sealed class ZeroAllocInjectServiceProviderFactory : IServiceProviderFactory<IServiceCollection>");
            sb.AppendLine("    {");
            sb.AppendLine("        public IServiceCollection CreateBuilder(IServiceCollection services) => services;");
            sb.AppendLine();
            sb.AppendLine("        public IServiceProvider CreateServiceProvider(IServiceCollection containerBuilder)");
            sb.AppendLine("        {");
            sb.AppendLine("            // Snapshot the collection so subsequent mutations don't leak into the lazily-built fallback.");
            sb.AppendLine("            IServiceCollection snapshot = new ServiceCollection();");
            sb.AppendLine("            foreach (var d in containerBuilder) snapshot.Add(d);");
            sb.AppendLine("            return new global::ZeroAlloc.Inject.Generated." + className + "(snapshot);");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            return sb.ToString();
        }

        private static string GenerateStandaloneServiceProviderClass(
            List<ServiceRegistrationInfo> services,
            string assemblyName,
            System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<DecoratorRegistrationInfo>> decoratorsByInterface,
            ImmutableArray<ClosedGenericFactoryInfo> closedGenericFactories)
        {
            // Clean assembly name for class naming
            var cleanName = new StringBuilder();
            foreach (var c in assemblyName)
            {
                if (c != '.' && c != '-' && c != '_')
                {
                    cleanName.Append(c);
                }
            }
            var className = cleanName.ToString() + "StandaloneServiceProvider";

            // Separate services by lifetime; collect open generics for OpenGenericMap
            var transients = new List<ServiceRegistrationInfo>();
            var singletons = new List<ServiceRegistrationInfo>();
            var scopeds = new List<ServiceRegistrationInfo>();
            var keyedServices = new List<ServiceRegistrationInfo>();
            var openGenerics = new List<ServiceRegistrationInfo>();

            foreach (var svc in services)
            {
                if (svc.IsOpenGeneric)
                {
                    openGenerics.Add(svc);
                    continue;
                }
                if (svc.Key != null)
                {
                    keyedServices.Add(svc);
                    continue;
                }
                if (svc.Lifetime == "Transient") transients.Add(svc);
                else if (svc.Lifetime == "Singleton") singletons.Add(svc);
                else if (svc.Lifetime == "Scoped") scopeds.Add(svc);
            }

            // All open generics are handled by explicit closed-type entries (FindClosedGenericUsages).
            // The runtime reflection machinery has been removed; open generics with no detected
            // closed usages will produce ZI018 (Task 6). The openGenerics list is kept for
            // diagnostic purposes only.

            // Group non-keyed services by service type for IEnumerable<T> support.
            var serviceTypeGroups = new Dictionary<string, List<ServiceTypeGroupEntry>>();

            for (int i = 0; i < transients.Count; i++)
            {
                var svc = transients[i];
                foreach (var st in GetServiceTypes(svc))
                {
                    if (!serviceTypeGroups.ContainsKey(st))
                        serviceTypeGroups[st] = new List<ServiceTypeGroupEntry>();
                    serviceTypeGroups[st].Add(new ServiceTypeGroupEntry(svc, "Transient", i));
                }
            }
            for (int i = 0; i < singletons.Count; i++)
            {
                var svc = singletons[i];
                foreach (var st in GetServiceTypes(svc))
                {
                    if (!serviceTypeGroups.ContainsKey(st))
                        serviceTypeGroups[st] = new List<ServiceTypeGroupEntry>();
                    serviceTypeGroups[st].Add(new ServiceTypeGroupEntry(svc, "Singleton", i));
                }
            }
            for (int i = 0; i < scopeds.Count; i++)
            {
                var svc = scopeds[i];
                foreach (var st in GetServiceTypes(svc))
                {
                    if (!serviceTypeGroups.ContainsKey(st))
                        serviceTypeGroups[st] = new List<ServiceTypeGroupEntry>();
                    serviceTypeGroups[st].Add(new ServiceTypeGroupEntry(svc, "Scoped", i));
                }
            }

            // Determine which entry is the last registration per service type (for last-wins behavior)
            var lastRegistrationPerType = new Dictionary<string, ServiceTypeGroupEntry>();
            foreach (var kvp in serviceTypeGroups)
            {
                lastRegistrationPerType[kvp.Key] = kvp.Value[kvp.Value.Count - 1];
            }

            // Pre-compute IEnumerable<T> cache field map for all-singleton groups (root level).
            // rootEntries excludes Scoped lifetime to match the root-level emit logic below.
            var enumerableCacheFields = new List<(string FieldName, string ServiceType)>();
            var cacheFieldByServiceType = new Dictionary<string, string>(StringComparer.Ordinal);
            {
                int counter = 0;
                foreach (var kvp in serviceTypeGroups)
                {
                    var rootEntriesForCache = new List<ServiceTypeGroupEntry>();
                    foreach (var entry in kvp.Value)
                        if (entry.Lifetime != "Scoped") rootEntriesForCache.Add(entry);
                    if (IsAllSingleton(rootEntriesForCache))
                    {
                        var fieldName = "_enumerable_" + SanitizeForFieldName(kvp.Key) + "_" + counter++;
                        enumerableCacheFields.Add((fieldName, kvp.Key));
                        cacheFieldByServiceType[kvp.Key] = fieldName;
                    }
                }
            }

            bool hasKeyedServices = keyedServices.Count > 0 || closedGenericFactories.Any(static cgf => cgf.Key != null);
            var decoratedEntries = CollectDecoratedEntries(services, decoratorsByInterface);

            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated />");
            // CS1591: the generated members are public but intentionally undocumented.
            // The `// <auto-generated />` marker above does NOT exempt a source-generator
            // tree from CS1591, and a consumer cannot pragma or annotate code it does not
            // own — so a consumer with GenerateDocumentationFile + WarningsAsErrors;CS1591
            // (a doc-coverage gate) would fail to build on our output. Disable 1591 in the
            // generated file itself, as the Razor generator does.
            sb.AppendLine("#pragma warning disable 1591");
            sb.AppendLine("#nullable enable");
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Threading;");
            sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
            sb.AppendLine();
            sb.AppendLine("namespace ZeroAlloc.Inject.Generated");
            sb.AppendLine("{");
            var baseClass = "global::ZeroAlloc.Inject.Container.ZeroAllocInjectStandaloneProvider";
            sb.AppendLine("    internal sealed class " + className + " : " + baseClass);
            sb.AppendLine("    {");

            // Separate keyed services by lifetime
            var keyedSingletons = new List<ServiceRegistrationInfo>();
            var keyedTransients = new List<ServiceRegistrationInfo>();
            var keyedScopedServices = new List<ServiceRegistrationInfo>();

            foreach (var svc in keyedServices)
            {
                if (svc.Lifetime == "Singleton") keyedSingletons.Add(svc);
                else if (svc.Lifetime == "Transient") keyedTransients.Add(svc);
                else if (svc.Lifetime == "Scoped") keyedScopedServices.Add(svc);
            }

            // Singleton fields
            for (int i = 0; i < singletons.Count; i++)
            {
                if (HasUndecoratedServiceType(decoratedEntries, singletons[i]))
                    sb.AppendLine("        private " + singletons[i].FullyQualifiedName + "? _singleton_" + i + ";");
            }
            // Keyed singleton fields
            for (int i = 0; i < keyedSingletons.Count; i++)
            {
                sb.AppendLine("        private " + keyedSingletons[i].FullyQualifiedName + "? _keyedSingleton_" + i + ";");
            }
            EmitClosedGenericSingletonFields(sb, closedGenericFactories);
            // IEnumerable<T> cache fields (one per all-singleton enumerable group)
            foreach (var (fieldName, fieldServiceType) in enumerableCacheFields)
            {
                sb.AppendLine("        private " + fieldServiceType + "[]? " + fieldName + ";");
            }
            if (singletons.Count > 0 || keyedSingletons.Count > 0 || closedGenericFactories.Length > 0 || enumerableCacheFields.Count > 0)
            {
                sb.AppendLine();
            }

            // Constructor - parameterless
            sb.AppendLine("        public " + className + "() { }");
            sb.AppendLine();

            // ResolveKnown - root provider: transients + singletons (scoped returns null)
            sb.AppendLine("        protected override object? ResolveKnown(Type serviceType)");
            sb.AppendLine("        {");

            // Transients
            foreach (var svc in transients)
            {
                var serviceTypes = GetServiceTypes(svc);
                foreach (var serviceType in serviceTypes)
                {
                    ServiceTypeGroupEntry lastEntry;
                    if (lastRegistrationPerType.TryGetValue(serviceType, out lastEntry)
                        && lastEntry.Svc == svc && lastEntry.Lifetime == "Transient")
                    {
                        sb.AppendLine("            if (serviceType == typeof(" + serviceType + "))");
                        if (FindDecorated(decoratedEntries, svc, serviceType) is { } decoratedTransient)
                        {
                            sb.AppendLine("                return " + DecoratedExpr(decoratedTransient, className: null) + ";");
                            continue;
                        }
                        var newExpr = BuildNewExpression(svc);
                        if (svc.PropertyInjections.Count > 0)
                        {
                            sb.AppendLine("            {");
                            sb.AppendLine("                var instance = " + newExpr + ";");
                            AppendPropertySetters(sb, svc.PropertyInjections, "                ");
                            sb.AppendLine(svc.ImplementsDisposable
                                ? "                return TrackDisposable(instance);"
                                : "                return instance;");
                            sb.AppendLine("            }");
                        }
                        else
                        {
                            sb.AppendLine("                return " + TrackIfDisposable(newExpr, svc.ImplementsDisposable) + ";");
                        }
                    }
                }
            }

            // Singletons
            for (int i = 0; i < singletons.Count; i++)
            {
                var svc = singletons[i];
                var fieldName = "_singleton_" + i;
                var newExpr = BuildNewExpression(svc);
                var serviceTypes = GetServiceTypes(svc);

                foreach (var serviceType in serviceTypes)
                {
                    ServiceTypeGroupEntry lastEntry;
                    if (!lastRegistrationPerType.TryGetValue(serviceType, out lastEntry)
                        || lastEntry.Svc != svc || lastEntry.Lifetime != "Singleton")
                    {
                        continue;
                    }

                    sb.AppendLine("            if (serviceType == typeof(" + serviceType + "))");
                    if (FindDecorated(decoratedEntries, svc, serviceType) is { } decoratedSingleton)
                    {
                        sb.AppendLine("                return " + DecoratedExpr(decoratedSingleton, className: null) + ";");
                        continue;
                    }
                    sb.AppendLine("            {");
                    sb.AppendLine("                if (" + fieldName + " != null) return " + fieldName + ";");
                    sb.AppendLine("                var instance = " + newExpr + ";");
                    AppendPropertySetters(sb, svc.PropertyInjections, "                ");
                    if (svc.ImplementsDisposable)
                    {
                        sb.AppendLine("                var existing = Interlocked.CompareExchange(ref " + fieldName + ", instance, null);");
                        sb.AppendLine("                if (existing != null) { (instance as System.IDisposable)?.Dispose(); return existing; }");
                        sb.AppendLine("                return TrackDisposable(instance);");
                    }
                    else
                    {
                        sb.AppendLine("                return Interlocked.CompareExchange(ref " + fieldName + ", instance, null) ?? " + fieldName + ";");
                    }
                    sb.AppendLine("            }");
                }
            }

            // IEnumerable<T> resolution
            foreach (var kvp in serviceTypeGroups)
            {
                var serviceType = kvp.Key;
                var entries = kvp.Value;

                // Root excludes scoped services
                var rootEntries = new List<ServiceTypeGroupEntry>();
                foreach (var entry in entries)
                {
                    if (entry.Lifetime != "Scoped")
                        rootEntries.Add(entry);
                }
                if (rootEntries.Count == 0) continue;

                sb.AppendLine("            if (serviceType == typeof(System.Collections.Generic.IEnumerable<" + serviceType + ">))");
                sb.AppendLine("            {");
                if (cacheFieldByServiceType.TryGetValue(serviceType, out var cacheFieldName))
                {
                    sb.Append("                return " + cacheFieldName + " ??= new " + serviceType + "[] { ");
                    for (int j = 0; j < rootEntries.Count; j++)
                    {
                        if (j > 0) sb.Append(", ");
                        sb.Append(EnumerableEntryExpr(decoratedEntries, rootEntries[j], serviceType, className: null));
                    }
                    sb.AppendLine(" };");
                }
                else
                {
                    sb.Append("                return new " + serviceType + "[] { ");

                    for (int j = 0; j < rootEntries.Count; j++)
                    {
                        if (j > 0) sb.Append(", ");
                        var entry = rootEntries[j];

                        if (FindDecorated(decoratedEntries, entry.Svc, serviceType) is { } decoratedEntry)
                        {
                            sb.Append(DecoratedExpr(decoratedEntry, className: null));
                        }
                        else if (entry.Lifetime == "Transient")
                        {
                            sb.Append(TrackIfDisposable(BuildNewExpression(entry.Svc), entry.Svc.ImplementsDisposable));
                        }
                        else if (entry.Lifetime == "Singleton")
                        {
                            sb.Append("(" + serviceType + ")GetService(typeof(" + entry.Svc.FullyQualifiedName + "))!");
                        }
                    }

                    sb.AppendLine(" };");
                }
                sb.AppendLine("            }");
            }

            EmitClosedGenericRoot(sb, closedGenericFactories, decoratorsByInterface);

            sb.AppendLine("            return null;");
            sb.AppendLine("        }");
            sb.AppendLine();

            EmitClosedGenericSingletonAccessors(sb, closedGenericFactories, decoratorsByInterface);
            EmitDecoratedMembers(sb, decoratedEntries, inScope: false);

            EmitIsKnownService(sb, serviceTypeGroups, closedGenericFactories);
            sb.AppendLine();

            EmitIsKnownKeyedService(sb, keyedServices, closedGenericFactories);
            sb.AppendLine();

            // Keyed services the generator knows; the base resolves any other key, and a null one.
            if (hasKeyedServices)
            {
                sb.AppendLine("        protected override object? ResolveKnownKeyed(Type serviceType, object serviceKey)");
                sb.AppendLine("        {");
                sb.AppendLine("            if (serviceKey is string key)");
                sb.AppendLine("            {");

                // Keyed singletons - cached with Interlocked.CompareExchange
                for (int i = 0; i < keyedSingletons.Count; i++)
                {
                    var svc = keyedSingletons[i];
                    var serviceTypes = GetServiceTypes(svc);
                    var newExpr = BuildNewExpression(svc);
                    var fieldName = "_keyedSingleton_" + i;
                    var escapedKey = svc.Key!.Replace("\\", "\\\\").Replace("\"", "\\\"");
                    foreach (var serviceType in serviceTypes)
                    {
                        sb.AppendLine("                if (serviceType == typeof(" + serviceType + ") && key == \"" + escapedKey + "\")");
                        sb.AppendLine("                {");
                        sb.AppendLine("                    if (" + fieldName + " != null) return " + fieldName + ";");
                        sb.AppendLine("                    var instance = " + newExpr + ";");
                        if (svc.PropertyInjections.Count > 0)
                        {
                            AppendPropertySetters(sb, svc.PropertyInjections, "                    ");
                        }
                        if (svc.ImplementsDisposable)
                        {
                            sb.AppendLine("                    var existing = Interlocked.CompareExchange(ref " + fieldName + ", instance, null);");
                            sb.AppendLine("                    if (existing != null) { (instance as System.IDisposable)?.Dispose(); return existing; }");
                            sb.AppendLine("                    return TrackDisposable(instance);");
                        }
                        else
                        {
                            sb.AppendLine("                    return Interlocked.CompareExchange(ref " + fieldName + ", instance, null) ?? " + fieldName + ";");
                        }
                        sb.AppendLine("                }");
                    }
                }

                // Keyed transients - new instance each call, tracked when disposable
                foreach (var svc in keyedTransients)
                {
                    var serviceTypes = GetServiceTypes(svc);
                    var newExpr = BuildNewExpression(svc);
                    var escapedKey = svc.Key!.Replace("\\", "\\\\").Replace("\"", "\\\"");
                    foreach (var serviceType in serviceTypes)
                    {
                        sb.AppendLine("                if (serviceType == typeof(" + serviceType + ") && key == \"" + escapedKey + "\")");
                        if (svc.PropertyInjections.Count > 0)
                        {
                            sb.AppendLine("                {");
                            sb.AppendLine("                    var instance = " + newExpr + ";");
                            AppendPropertySetters(sb, svc.PropertyInjections, "                    ");
                            sb.AppendLine(svc.ImplementsDisposable
                                ? "                    return TrackDisposable(instance);"
                                : "                    return instance;");
                            sb.AppendLine("                }");
                        }
                        else
                        {
                            sb.AppendLine("                    return " + TrackIfDisposable(newExpr, svc.ImplementsDisposable) + ";");
                        }
                    }
                }

                EmitKeyedClosedGenericBranches(sb, closedGenericFactories, decoratorsByInterface, className: null, "                ");

                sb.AppendLine("            }");
                sb.AppendLine("            return null;");
                sb.AppendLine("        }");
                sb.AppendLine();
            }

            // CreateScopeCore - no parameter for standalone
            sb.AppendLine("        protected override global::ZeroAlloc.Inject.Container.ZeroAllocInjectStandaloneScope CreateScopeCore()");
            sb.AppendLine("        {");
            sb.AppendLine("            return new Scope(this);");
            sb.AppendLine("        }");
            sb.AppendLine();

            // Nested Scope class
            var scopeBase = "global::ZeroAlloc.Inject.Container.ZeroAllocInjectStandaloneScope";
            sb.AppendLine("        private sealed class Scope : " + scopeBase);
            sb.AppendLine("        {");

            // Scoped fields
            for (int i = 0; i < scopeds.Count; i++)
            {
                if (HasUndecoratedServiceType(decoratedEntries, scopeds[i]))
                    sb.AppendLine("            private " + scopeds[i].FullyQualifiedName + "? _scoped_" + i + ";");
            }
            for (int i = 0; i < keyedScopedServices.Count; i++)
            {
                sb.AppendLine("            private " + keyedScopedServices[i].FullyQualifiedName + "? _keyedScoped_" + i + ";");
            }
            bool hasScopedCgFields = EmitClosedGenericScopedFields(sb, closedGenericFactories);
            if (scopeds.Count > 0 || keyedScopedServices.Count > 0 || hasScopedCgFields)
            {
                sb.AppendLine();
            }

            // Scope constructor - only root, no fallbackScope
            sb.AppendLine("            public Scope(" + className + " root) : base(root) { }");
            sb.AppendLine();
            EmitDecoratedMembers(sb, decoratedEntries, inScope: true);

            // ResolveScopedKnown
            sb.AppendLine("            protected override object? ResolveScopedKnown(Type serviceType)");
            sb.AppendLine("            {");

            // Transients in scope - fresh instance each call
            foreach (var svc in transients)
            {
                var serviceTypes = GetServiceTypes(svc);
                foreach (var serviceType in serviceTypes)
                {
                    ServiceTypeGroupEntry lastEntry;
                    if (lastRegistrationPerType.TryGetValue(serviceType, out lastEntry)
                        && lastEntry.Svc == svc && lastEntry.Lifetime == "Transient")
                    {
                        sb.AppendLine("                if (serviceType == typeof(" + serviceType + "))");
                        if (FindDecorated(decoratedEntries, svc, serviceType) is { } decoratedTransient)
                        {
                            sb.AppendLine("                    return " + DecoratedExpr(decoratedTransient, className) + ";");
                            continue;
                        }
                        var newExpr = BuildNewExpressionForScope(svc);
                        if (svc.PropertyInjections.Count > 0)
                        {
                            sb.AppendLine("                {");
                            sb.AppendLine("                    var instance = " + newExpr + ";");
                            AppendPropertySetters(sb, svc.PropertyInjections, "                    ");
                            if (svc.ImplementsDisposable)
                            {
                                sb.AppendLine("                    return TrackDisposable(instance);");
                            }
                            else
                            {
                                sb.AppendLine("                    return instance;");
                            }
                            sb.AppendLine("                }");
                        }
                        else
                        {
                            if (svc.ImplementsDisposable)
                            {
                                newExpr = "TrackDisposable(" + newExpr + ")";
                            }
                            sb.AppendLine("                    return " + newExpr + ";");
                        }
                    }
                }
            }

            // Singletons in scope - delegate to Root
            foreach (var svc in singletons)
            {
                var serviceTypes = GetServiceTypes(svc);
                foreach (var serviceType in serviceTypes)
                {
                    ServiceTypeGroupEntry lastEntry;
                    if (!lastRegistrationPerType.TryGetValue(serviceType, out lastEntry)
                        || lastEntry.Svc != svc || lastEntry.Lifetime != "Singleton")
                    {
                        continue;
                    }
                    sb.AppendLine("                if (serviceType == typeof(" + serviceType + "))");
                    sb.AppendLine("                    return Root.GetService(serviceType);");
                }
            }

            // Scoped services
            for (int i = 0; i < scopeds.Count; i++)
            {
                var svc = scopeds[i];
                var fieldName = "_scoped_" + i;
                var innerExpr = BuildNewExpressionForScope(svc);
                var serviceTypes = GetServiceTypes(svc);

                foreach (var serviceType in serviceTypes)
                {
                    ServiceTypeGroupEntry lastEntry;
                    if (!lastRegistrationPerType.TryGetValue(serviceType, out lastEntry)
                        || lastEntry.Svc != svc || lastEntry.Lifetime != "Scoped")
                    {
                        continue;
                    }

                    sb.AppendLine("                if (serviceType == typeof(" + serviceType + "))");
                    sb.AppendLine("                {");
                    if (FindDecorated(decoratedEntries, svc, serviceType) is { } decoratedScoped)
                    {
                        sb.AppendLine("                    return " + DecoratedExpr(decoratedScoped, className) + ";");
                    }
                    else if (svc.ImplementsDisposable)
                    {
                        if (svc.PropertyInjections.Count > 0)
                        {
                            sb.AppendLine("                    if (" + fieldName + " == null) { " + fieldName + " = " + innerExpr + ";");
                            AppendPropertySetters(sb, svc.PropertyInjections, "                        ", fieldName);
                            sb.AppendLine("                        TrackDisposable(" + fieldName + "); }");
                        }
                        else
                        {
                            sb.AppendLine("                    if (" + fieldName + " == null) { " + fieldName + " = " + innerExpr + "; TrackDisposable(" + fieldName + "); }");
                        }
                        sb.AppendLine("                    return " + fieldName + ";");
                    }
                    else
                    {
                        if (svc.PropertyInjections.Count > 0)
                        {
                            sb.AppendLine("                    if (" + fieldName + " == null) { " + fieldName + " = " + innerExpr + ";");
                            AppendPropertySetters(sb, svc.PropertyInjections, "                        ", fieldName);
                            sb.AppendLine("                    }");
                        }
                        else
                        {
                            sb.AppendLine("                    if (" + fieldName + " == null) " + fieldName + " = " + innerExpr + ";");
                        }
                        sb.AppendLine("                    return " + fieldName + ";");
                    }
                    sb.AppendLine("                }");
                }
            }

            // IEnumerable<T> resolution in scope (all lifetimes)
            foreach (var kvp in serviceTypeGroups)
            {
                var serviceType = kvp.Key;
                var entries = kvp.Value;
                if (entries.Count == 0) continue;

                sb.AppendLine("                if (serviceType == typeof(System.Collections.Generic.IEnumerable<" + serviceType + ">))");
                sb.AppendLine("                {");

                if (IsAllSingleton(entries))
                {
                    // All entries are singletons — delegate to root provider's cached IEnumerable<T> path.
                    // No scope-level disposal tracking needed (singletons are tracked at provider scope).
                    sb.AppendLine("                    return Root.GetService(typeof(System.Collections.Generic.IEnumerable<" + serviceType + ">));");
                }
                else
                {
                    sb.Append("                    return new " + serviceType + "[] { ");

                    for (int j = 0; j < entries.Count; j++)
                    {
                        if (j > 0) sb.Append(", ");
                        var entry = entries[j];

                        if (FindDecorated(decoratedEntries, entry.Svc, serviceType) is { } decoratedEntry)
                        {
                            sb.Append(DecoratedExpr(decoratedEntry, className));
                        }
                        else if (entry.Lifetime == "Transient")
                        {
                            var newExpr = BuildNewExpressionForScope(entry.Svc);
                            if (entry.Svc.ImplementsDisposable)
                            {
                                sb.Append("TrackDisposable(" + newExpr + ")");
                            }
                            else
                            {
                                sb.Append(newExpr);
                            }
                        }
                        else if (entry.Lifetime == "Singleton")
                        {
                            sb.Append("(" + serviceType + ")Root.GetService(typeof(" + entry.Svc.FullyQualifiedName + "))!");
                        }
                        else if (entry.Lifetime == "Scoped")
                        {
                            var fieldName = "_scoped_" + entry.FieldIndex;
                            var newExpr = BuildNewExpressionForScope(entry.Svc);
                            if (entry.Svc.ImplementsDisposable)
                            {
                                sb.Append(fieldName + " ?? (" + fieldName + " = TrackDisposable(" + newExpr + "))");
                            }
                            else
                            {
                                sb.Append(fieldName + " ?? (" + fieldName + " = " + newExpr + ")");
                            }
                        }
                    }

                    sb.AppendLine(" };");
                }
                sb.AppendLine("                }");
            }

            EmitClosedGenericScope(sb, closedGenericFactories, decoratorsByInterface, className);

            sb.AppendLine("                return null;");
            sb.AppendLine("            }");


            // Keyed services the generator knows, in scope; the base resolves any other key.
            if (hasKeyedServices)
            {
                sb.AppendLine();
                sb.AppendLine("            protected override object? ResolveScopedKnownKeyed(Type serviceType, object serviceKey)");
                sb.AppendLine("            {");
                sb.AppendLine("                if (serviceKey is string key)");
                sb.AppendLine("                {");

                // Keyed singletons - delegate to root
                foreach (var svc in keyedSingletons)
                {
                    var serviceTypes = GetServiceTypes(svc);
                    var escapedKey = svc.Key!.Replace("\\", "\\\\").Replace("\"", "\\\"");
                    foreach (var serviceType in serviceTypes)
                    {
                        sb.AppendLine("                    if (serviceType == typeof(" + serviceType + ") && key == \"" + escapedKey + "\")");
                        sb.AppendLine("                        return ((" + className + ")Root).ResolveKnownKeyed(serviceType, serviceKey);");
                    }
                }

                // Keyed scoped services - cached per scope
                for (int i = 0; i < keyedScopedServices.Count; i++)
                {
                    var svc = keyedScopedServices[i];
                    var serviceTypes = GetServiceTypes(svc);
                    var escapedKey = svc.Key!.Replace("\\", "\\\\").Replace("\"", "\\\"");
                    var fieldName = "_keyedScoped_" + i;
                    var newExpr = BuildNewExpressionForScope(svc);
                    foreach (var serviceType in serviceTypes)
                    {
                        sb.AppendLine("                    if (serviceType == typeof(" + serviceType + ") && key == \"" + escapedKey + "\")");
                        sb.AppendLine("                    {");
                        if (svc.ImplementsDisposable)
                        {
                            sb.AppendLine("                        if (" + fieldName + " == null) { " + fieldName + " = " + newExpr + "; TrackDisposable(" + fieldName + "); }");
                        }
                        else
                        {
                            sb.AppendLine("                        if (" + fieldName + " == null) " + fieldName + " = " + newExpr + ";");
                        }
                        sb.AppendLine("                        return " + fieldName + ";");
                        sb.AppendLine("                    }");
                    }
                }

                // Keyed transients - fresh instance, track disposable if needed
                foreach (var svc in keyedTransients)
                {
                    var serviceTypes = GetServiceTypes(svc);
                    var escapedKey = svc.Key!.Replace("\\", "\\\\").Replace("\"", "\\\"");
                    var newExpr = BuildNewExpressionForScope(svc);
                    if (svc.ImplementsDisposable)
                    {
                        newExpr = "TrackDisposable(" + newExpr + ")";
                    }
                    foreach (var serviceType in serviceTypes)
                    {
                        sb.AppendLine("                    if (serviceType == typeof(" + serviceType + ") && key == \"" + escapedKey + "\")");
                        sb.AppendLine("                        return " + newExpr + ";");
                    }
                }

                EmitKeyedClosedGenericBranches(sb, closedGenericFactories, decoratorsByInterface, className, "                    ");

                sb.AppendLine("                }");
                sb.AppendLine("                return null;");
                sb.AppendLine("            }");
            }

            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            return sb.ToString();
        }

        /// <summary>
        /// A new instance from a generated root or scope, passed to TrackDisposable when disposable, so
        /// the container disposes it with itself as Microsoft DI does.
        /// </summary>
        private static string TrackIfDisposable(string newExpr, bool implementsDisposable) =>
            implementsDisposable ? "TrackDisposable(" + newExpr + ")" : newExpr;

        private static void EmitIsKnownService(
            StringBuilder sb,
            Dictionary<string, List<ServiceTypeGroupEntry>> serviceTypeGroups,
            ImmutableArray<ClosedGenericFactoryInfo> closedGenericFactories)
        {
            sb.AppendLine("        protected override bool IsKnownService(global::System.Type serviceType)");
            sb.AppendLine("        {");

            foreach (var kvp in serviceTypeGroups)
            {
                sb.AppendLine("            if (serviceType == typeof(" + kvp.Key + ")) return true;");
            }

            // The closed forms of open generics that constructors ask for
            foreach (var cgf in closedGenericFactories)
            {
                if (!cgf.IsResolved || cgf.Key != null || serviceTypeGroups.ContainsKey(cgf.InterfaceFqn)) continue;
                sb.AppendLine("            if (serviceType == typeof(" + cgf.InterfaceFqn + ")) return true;");
            }

            sb.AppendLine("            return false;");
            sb.AppendLine("        }");
        }

        /// <summary>
        /// The fields of a generated container that hold closed generic singletons, typed as the
        /// service, since a decorated one holds its outermost decorator.
        /// </summary>
        private static void EmitClosedGenericSingletonFields(
            StringBuilder sb,
            ImmutableArray<ClosedGenericFactoryInfo> closedGenericFactories)
        {
            for (int i = 0; i < closedGenericFactories.Length; i++)
            {
                var cgf = closedGenericFactories[i];
                if (string.Equals(cgf.Lifetime, "Singleton", StringComparison.Ordinal))
                    sb.AppendLine("        private " + cgf.InterfaceFqn + "? _cg_s_" + i + ";");
            }
        }

        /// <summary>The fields of a generated scope that hold its closed generic scoped instances.</summary>
        private static bool EmitClosedGenericScopedFields(
            StringBuilder sb,
            ImmutableArray<ClosedGenericFactoryInfo> closedGenericFactories)
        {
            bool any = false;
            for (int i = 0; i < closedGenericFactories.Length; i++)
            {
                var cgf = closedGenericFactories[i];
                if (!string.Equals(cgf.Lifetime, "Scoped", StringComparison.Ordinal)) continue;
                sb.AppendLine("            private " + cgf.InterfaceFqn + "? _cg_sc_" + i + ";");
                any = true;
            }
            return any;
        }

        /// <summary>
        /// A generated container's root branches for the unkeyed closed forms of open generics: the
        /// one resolving a closed form returns, and IEnumerable&lt;T&gt; of every registration in order,
        /// as Microsoft DI returns them. A scoped registration is not resolved at the root, as for any
        /// scoped service, and neither is IEnumerable&lt;T&gt; of a closed form that has one.
        /// </summary>
        private static void EmitClosedGenericRoot(
            StringBuilder sb,
            ImmutableArray<ClosedGenericFactoryInfo> closedGenericFactories,
            Dictionary<string, List<DecoratorRegistrationInfo>> decoratorsByInterface)
        {
            for (int i = 0; i < closedGenericFactories.Length; i++)
            {
                var cgf = closedGenericFactories[i];
                if (!cgf.IsResolved || cgf.Key != null || string.Equals(cgf.Lifetime, "Scoped", StringComparison.Ordinal)) continue;
                sb.AppendLine("            if (serviceType == typeof(" + cgf.InterfaceFqn + "))");
                sb.AppendLine("                return " + ClosedGenericExpr(closedGenericFactories, i, decoratorsByInterface, className: null) + ";");
            }

            foreach (var closedForm in GroupClosedForms(closedGenericFactories))
            {
                if (closedForm.Exists(i => string.Equals(closedGenericFactories[i].Lifetime, "Scoped", StringComparison.Ordinal))) continue;
                EmitClosedGenericEnumerable(sb, closedGenericFactories, closedForm, decoratorsByInterface, className: null, "            ");
            }
        }

        /// <summary>A generated scope's branches for the unkeyed closed forms of open generics.</summary>
        private static void EmitClosedGenericScope(
            StringBuilder sb,
            ImmutableArray<ClosedGenericFactoryInfo> closedGenericFactories,
            Dictionary<string, List<DecoratorRegistrationInfo>> decoratorsByInterface,
            string className)
        {
            for (int i = 0; i < closedGenericFactories.Length; i++)
            {
                var cgf = closedGenericFactories[i];
                if (!cgf.IsResolved || cgf.Key != null) continue;
                sb.AppendLine("                if (serviceType == typeof(" + cgf.InterfaceFqn + "))");
                sb.AppendLine("                    return " + ClosedGenericExpr(closedGenericFactories, i, decoratorsByInterface, className) + ";");
            }

            foreach (var closedForm in GroupClosedForms(closedGenericFactories))
            {
                EmitClosedGenericEnumerable(sb, closedGenericFactories, closedForm, decoratorsByInterface, className, "                ");
            }
        }

        /// <summary>
        /// A generated container's GetKeyedService branches for the keyed closed forms of open
        /// generics, in the root with a null <paramref name="className"/> or in its scope. As for a
        /// keyed non-generic service, a scoped one is not resolved at the root.
        /// </summary>
        private static void EmitKeyedClosedGenericBranches(
            StringBuilder sb,
            ImmutableArray<ClosedGenericFactoryInfo> closedGenericFactories,
            Dictionary<string, List<DecoratorRegistrationInfo>> decoratorsByInterface,
            string? className,
            string indent)
        {
            for (int i = 0; i < closedGenericFactories.Length; i++)
            {
                var cgf = closedGenericFactories[i];
                if (!cgf.IsResolved || cgf.Key == null) continue;
                if (className == null && string.Equals(cgf.Lifetime, "Scoped", StringComparison.Ordinal)) continue;
                sb.AppendLine(indent + "if (serviceType == typeof(" + cgf.InterfaceFqn + ") && key == \"" + EscapeKey(cgf.Key) + "\")");
                sb.AppendLine(indent + "    return " + ClosedGenericExpr(closedGenericFactories, i, decoratorsByInterface, className) + ";");
            }
        }

        private static void EmitClosedGenericEnumerable(
            StringBuilder sb,
            ImmutableArray<ClosedGenericFactoryInfo> closedGenericFactories,
            List<int> closedForm,
            Dictionary<string, List<DecoratorRegistrationInfo>> decoratorsByInterface,
            string? className,
            string indent)
        {
            var serviceType = closedGenericFactories[closedForm[0]].InterfaceFqn;
            sb.AppendLine(indent + "if (serviceType == typeof(System.Collections.Generic.IEnumerable<" + serviceType + ">))");
            sb.Append(indent + "    return new " + serviceType + "[] { ");
            for (int j = 0; j < closedForm.Count; j++)
            {
                if (j > 0) sb.Append(", ");
                sb.Append(ClosedGenericExpr(closedGenericFactories, closedForm[j], decoratorsByInterface, className));
            }
            sb.AppendLine(" };");
        }

        /// <summary>
        /// The instance of one closed generic registration in a generated root, with a null
        /// <paramref name="className"/>, or in its scope: a transient is constructed and tracked for
        /// disposal; a singleton comes from the root; a scoped one is cached in the scope.
        /// </summary>
        private static string ClosedGenericExpr(
            ImmutableArray<ClosedGenericFactoryInfo> closedGenericFactories,
            int index,
            Dictionary<string, List<DecoratorRegistrationInfo>> decoratorsByInterface,
            string? className)
        {
            var cgf = closedGenericFactories[index];
            if (string.Equals(cgf.Lifetime, "Singleton", StringComparison.Ordinal))
            {
                return className == null
                    ? "ClosedGenericSingleton" + index + "()"
                    : "((" + className + ")Root).ClosedGenericSingleton" + index + "()";
            }

            var newExpr = BuildClosedGenericInstanceExpr(closedGenericFactories, cgf, decoratorsByInterface, ContainerResolve);
            var tracked = TrackIfDisposable(newExpr, ClosedGenericIsDisposable(cgf, decoratorsByInterface));
            if (string.Equals(cgf.Lifetime, "Scoped", StringComparison.Ordinal))
            {
                return "(_cg_sc_" + index + " ??= " + tracked + ")";
            }
            return tracked;
        }

        /// <summary>The indices of each unkeyed closed form's registrations, closed form by closed form.</summary>
        private static List<List<int>> GroupClosedForms(ImmutableArray<ClosedGenericFactoryInfo> closedGenericFactories)
        {
            var groups = new List<List<int>>();
            var byServiceType = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            for (int i = 0; i < closedGenericFactories.Length; i++)
            {
                if (closedGenericFactories[i].Key != null) continue;
                if (!byServiceType.TryGetValue(closedGenericFactories[i].InterfaceFqn, out var group))
                {
                    group = new List<int>();
                    byServiceType[closedGenericFactories[i].InterfaceFqn] = group;
                    groups.Add(group);
                }
                group.Add(i);
            }
            return groups;
        }

        /// <summary>
        /// One accessor per closed generic singleton in a generated container. It creates the instance
        /// once, and the single and IEnumerable&lt;T&gt; branches of the root and of every scope share it.
        /// The instance that loses a race is disposed, unless it is decorated: its inner is the shared
        /// concrete singleton, which a decorator may dispose with itself.
        /// </summary>
        private static void EmitClosedGenericSingletonAccessors(
            StringBuilder sb,
            ImmutableArray<ClosedGenericFactoryInfo> closedGenericFactories,
            Dictionary<string, List<DecoratorRegistrationInfo>> decoratorsByInterface)
        {
            for (int i = 0; i < closedGenericFactories.Length; i++)
            {
                var cgf = closedGenericFactories[i];
                if (!string.Equals(cgf.Lifetime, "Singleton", StringComparison.Ordinal)) continue;
                var field = "_cg_s_" + i;
                sb.AppendLine("        private " + cgf.InterfaceFqn + " ClosedGenericSingleton" + i + "()");
                sb.AppendLine("        {");
                sb.AppendLine("            var existing = " + field + ";");
                sb.AppendLine("            if (existing != null) return existing;");
                sb.AppendLine("            " + cgf.InterfaceFqn + " instance = " + BuildClosedGenericInstanceExpr(closedGenericFactories, cgf, decoratorsByInterface, ContainerResolve) + ";");
                sb.AppendLine("            var winner = Interlocked.CompareExchange(ref " + field + ", instance, null);");
                sb.AppendLine(ClosedGenericIsDisposable(cgf, decoratorsByInterface)
                    ? "            if (winner == null) return TrackDisposable(instance);"
                    : "            if (winner == null) return instance;");
                if (ClosedGenericDecorators(cgf, decoratorsByInterface) == null && cgf.ImplementsDisposable)
                    sb.AppendLine("            (instance as global::System.IDisposable)?.Dispose();");
                sb.AppendLine("            return winner;");
                sb.AppendLine("        }");
                sb.AppendLine();
            }
        }

        private static void EmitIsKnownKeyedService(
            StringBuilder sb,
            List<ServiceRegistrationInfo> keyedServices,
            ImmutableArray<ClosedGenericFactoryInfo> closedGenericFactories)
        {
            sb.AppendLine("        protected override bool IsKnownKeyedService(global::System.Type serviceType, object? serviceKey)");
            sb.AppendLine("        {");

            bool hasKeyedClosedForms = closedGenericFactories.Any(static cgf => cgf.Key != null);
            if (keyedServices.Count > 0 || hasKeyedClosedForms)
            {
                sb.AppendLine("            if (serviceKey is string key)");
                sb.AppendLine("            {");

                foreach (var svc in keyedServices)
                {
                    var serviceTypes = GetServiceTypes(svc);
                    var escapedKey = EscapeKey(svc.Key!);
                    foreach (var serviceType in serviceTypes)
                    {
                        sb.AppendLine("                if (serviceType == typeof(" + serviceType + ") && key == \"" + escapedKey + "\") return true;");
                    }
                }

                foreach (var cgf in closedGenericFactories)
                {
                    if (!cgf.IsResolved || cgf.Key == null) continue;
                    sb.AppendLine("                if (serviceType == typeof(" + cgf.InterfaceFqn + ") && key == \"" + EscapeKey(cgf.Key) + "\") return true;");
                }

                sb.AppendLine("            }");
            }

            sb.AppendLine("            return false;");
            sb.AppendLine("        }");
        }

        /// <summary>A service key as the content of a C# string literal.</summary>
        private static string EscapeKey(string key) => key.Replace("\\", "\\\\").Replace("\"", "\\\"");

        private static List<string> GetServiceTypes(ServiceRegistrationInfo svc)
        {
            var types = new List<string>();
            if (svc.AsType != null)
            {
                types.Add(svc.AsType);
            }
            else
            {
                foreach (var iface in svc.Interfaces)
                {
                    types.Add(iface);
                }
                // Concrete type
                types.Add(svc.FullyQualifiedName);
            }
            return types;
        }

        private static void DetectCircularDependencies(
            List<DiagnosticInfo> diagnostics,
            List<ServiceRegistrationInfo> allServices,
            Dictionary<string, System.Collections.Generic.List<DecoratorRegistrationInfo>> decoratorsByInterface)
        {
            // Build service type -> ServiceRegistrationInfo lookup
            var serviceByType = new Dictionary<string, ServiceRegistrationInfo>();
            foreach (var svc in allServices)
            {
                foreach (var st in GetServiceTypes(svc))
                {
                    serviceByType[st] = svc; // last-wins
                }
            }

            // Build adjacency list
            var adjacency = new Dictionary<string, List<string>>();
            foreach (var svc in allServices)
            {
                var deps = new List<string>();
                foreach (var param in svc.ConstructorParameters)
                {
                    if (param.IsOptional) continue;
                    if (serviceByType.ContainsKey(param.FullyQualifiedTypeName))
                    {
                        deps.Add(param.FullyQualifiedTypeName);
                    }
                }
                foreach (var st in GetServiceTypes(svc))
                {
                    adjacency[st] = deps;
                }
            }

            // Add decorator edges
            foreach (var kvp in decoratorsByInterface)
            {
                var interfaceFqn = kvp.Key;
                foreach (var dec in kvp.Value)
                {
                    foreach (var param in dec.ConstructorParameters)
                    {
                        if (param.IsOptional) continue;
                        if (param.FullyQualifiedTypeName == dec.DecoratedInterfaceFqn) continue;
                        if (serviceByType.ContainsKey(param.FullyQualifiedTypeName))
                        {
                            if (adjacency.TryGetValue(interfaceFqn, out var existing))
                            {
                                existing.Add(param.FullyQualifiedTypeName);
                            }
                        }
                    }
                }
            }

            // DFS cycle detection
            var color = new Dictionary<string, int>();
            var parent = new Dictionary<string, string?>();
            foreach (var key in adjacency.Keys)
            {
                color[key] = 0;
                parent[key] = null;
            }

            var reportedCycles = new System.Collections.Generic.HashSet<string>();

            foreach (var node in adjacency.Keys.ToList())
            {
                if (color.TryGetValue(node, out var c) && c == 0)
                {
                    DfsCycleDetect(node, adjacency, color, parent, serviceByType, diagnostics, reportedCycles);
                }
            }
        }

        private static void DfsCycleDetect(
            string node,
            Dictionary<string, List<string>> adjacency,
            Dictionary<string, int> color,
            Dictionary<string, string?> parent,
            Dictionary<string, ServiceRegistrationInfo> serviceByType,
            List<DiagnosticInfo> diagnostics,
            System.Collections.Generic.HashSet<string> reportedCycles)
        {
            color[node] = 1; // gray

            if (adjacency.TryGetValue(node, out var deps))
            {
                foreach (var dep in deps)
                {
                    if (!color.ContainsKey(dep))
                    {
                        color[dep] = 0;
                    }

                    if (color[dep] == 0)
                    {
                        parent[dep] = node;
                        DfsCycleDetect(dep, adjacency, color, parent, serviceByType, diagnostics, reportedCycles);
                    }
                    else if (color[dep] == 1)
                    {
                        // Cycle found - reconstruct path
                        var cycle = new List<string> { dep };
                        var current = node;
                        while (current != null && current != dep)
                        {
                            cycle.Add(current);
                            parent.TryGetValue(current, out current);
                        }
                        cycle.Add(dep);
                        cycle.Reverse();
                        var cyclePath = string.Join(" \u2192 ", cycle);

                        if (reportedCycles.Add(cyclePath))
                        {
                            // ZAI014 is reported at the class of the first service in the path, with
                            // the other classes of the cycle as additional locations.
                            var locations = new List<LocationInfo>();
                            for (int i = 0; i < cycle.Count - 1; i++)
                            {
                                if (serviceByType.TryGetValue(cycle[i], out var member)
                                    && member.Location != null
                                    && !locations.Contains(member.Location))
                                {
                                    locations.Add(member.Location);
                                }
                            }

                            diagnostics.Add(new DiagnosticInfo(
                                DiagnosticDescriptors.CircularDependency,
                                locations.Count > 0 ? locations[0] : null,
                                locations.Count > 1
                                    ? ImmutableArray.CreateRange(locations.Skip(1))
                                    : ImmutableArray<LocationInfo>.Empty,
                                cyclePath));
                        }
                    }
                }
            }

            color[node] = 2; // black
        }

        /// <summary>
        /// Appends property setter statements into a block body for the fast-path code-gen.
        /// Each setter is placed on its own line with the given <paramref name="indent"/>.
        /// Uses <c>GetService(typeof(T))</c> which is available on the generated provider/scope class.
        /// </summary>
        private static void AppendPropertySetters(
            StringBuilder sb,
            List<PropertyInjectionInfo> propertyInjections,
            string indent,
            string targetName = "instance")
        {
            foreach (var prop in propertyInjections)
            {
                sb.Append(indent)
                  .Append(targetName).Append(".")
                  .Append(prop.PropertyName)
                  .Append(" = (")
                  .Append(prop.FullyQualifiedTypeName)
                  .Append(prop.IsRequired ? ")" : "?)")
                  .Append("GetService(typeof(")
                  .Append(prop.FullyQualifiedTypeName)
                  .Append("))")
                  .AppendLine(prop.IsRequired ? "!;" : ";");
            }
        }

        private static bool IsAllSingleton(System.Collections.Generic.List<ServiceTypeGroupEntry> entries)
        {
            if (entries.Count == 0) return false;
            foreach (var e in entries)
                if (e.Lifetime != "Singleton") return false;
            return true;
        }

        private static string SanitizeForFieldName(string typeName)
        {
            var sb = new System.Text.StringBuilder(typeName.Length);
            foreach (var c in typeName)
                sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
            return sb.ToString();
        }

        private static string BuildNewExpression(ServiceRegistrationInfo svc)
        {
            return BuildNewExpressionCore(svc, false);
        }

        private static string BuildNewExpressionForScope(ServiceRegistrationInfo svc)
        {
            return BuildNewExpressionCore(svc, true);
        }

        private static string BuildNewExpressionCore(ServiceRegistrationInfo svc, bool isScope)
        {
            if (svc.ConstructorParameters.Count == 0)
            {
                return "new " + svc.FullyQualifiedName + "()";
            }

            var argSb = new StringBuilder();
            argSb.Append("new ");
            argSb.Append(svc.FullyQualifiedName);
            argSb.Append("(");

            for (int i = 0; i < svc.ConstructorParameters.Count; i++)
            {
                var param = svc.ConstructorParameters[i];
                if (i > 0)
                {
                    argSb.Append(", ");
                }
                if (param.IsOptional)
                {
                    argSb.Append("(");
                    argSb.Append(param.FullyQualifiedTypeName);
                    argSb.Append("?)GetService(typeof(");
                    argSb.Append(param.FullyQualifiedTypeName);
                    argSb.Append("))");
                }
                else
                {
                    argSb.Append("(");
                    argSb.Append(param.FullyQualifiedTypeName);
                    argSb.Append(")GetService(typeof(");
                    argSb.Append(param.FullyQualifiedTypeName);
                    argSb.Append("))!");
                }
            }

            argSb.Append(")");
            return argSb.ToString();
        }

        // ---- Closed generic factory code-gen helpers ----

        /// <summary>How a generated container resolves a dependency: through its own GetService.</summary>
        private static string ContainerResolve(string typeFqn, bool optional) => optional
            ? "(" + typeFqn + "?)GetService(typeof(" + typeFqn + "))"
            : "(" + typeFqn + ")GetService(typeof(" + typeFqn + "))!";

        /// <summary>How a factory in the generated Add...Services extension resolves a dependency.</summary>
        private static string ServiceProviderResolve(string typeFqn, bool optional) => optional
            ? "sp.GetService<" + typeFqn + ">()"
            : "sp.GetRequiredService<" + typeFqn + ">()";

        /// <summary>Builds a "new ClosedImpl(args...)" expression for a closed generic registration.</summary>
        private static string BuildClosedGenericNewExpr(ClosedGenericFactoryInfo cgf, Func<string, bool, string> resolve)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("new ").Append(cgf.ImplementationFqn).Append("(");
            for (int i = 0; i < cgf.Parameters.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                var p = cgf.Parameters[i];
                sb.Append(resolve(p.FullyQualifiedTypeName, p.IsOptional));
            }
            sb.Append(")");
            return sb.ToString();
        }

        /// <summary>
        /// The decorators of a closed generic registration, innermost first, or null when it has none.
        /// A keyed registration is not decorated, as a keyed non-generic service is not.
        /// </summary>
        private static List<DecoratorRegistrationInfo>? ClosedGenericDecorators(
            ClosedGenericFactoryInfo cgf,
            Dictionary<string, List<DecoratorRegistrationInfo>> decoratorsByInterface)
        {
            if (cgf.Key != null) return null;
            return decoratorsByInterface.TryGetValue(cgf.OpenServiceFqn, out var decorators) && decorators.Count > 0
                ? decorators
                : null;
        }

        /// <summary>
        /// Whether the instance a closed generic registration creates is disposable: its outermost
        /// decorator, or the implementation when it has none.
        /// </summary>
        private static bool ClosedGenericIsDisposable(
            ClosedGenericFactoryInfo cgf,
            Dictionary<string, List<DecoratorRegistrationInfo>> decoratorsByInterface)
        {
            var decorators = ClosedGenericDecorators(cgf, decoratorsByInterface);
            return decorators == null ? cgf.ImplementsDisposable : decorators[decorators.Count - 1].ImplementsDisposable;
        }

        /// <summary>
        /// The instance expression of a closed generic registration. A decorated one chains its
        /// decorators around the concrete closed registration of its implementation, as the
        /// Add...Services extension does for a non-generic decorated service, so the inner has that
        /// registration's lifetime and is disposed with it. When As leaves the implementation
        /// unregistered, the decorators wrap a new instance.
        /// </summary>
        private static string BuildClosedGenericInstanceExpr(
            ImmutableArray<ClosedGenericFactoryInfo> closedGenericFactories,
            ClosedGenericFactoryInfo cgf,
            Dictionary<string, List<DecoratorRegistrationInfo>> decoratorsByInterface,
            Func<string, bool, string> resolve)
        {
            var decorators = ClosedGenericDecorators(cgf, decoratorsByInterface);
            if (decorators == null) return BuildClosedGenericNewExpr(cgf, resolve);

            bool concreteIsRegistered = false;
            foreach (var other in closedGenericFactories)
            {
                if (other.Key == null && string.Equals(other.InterfaceFqn, cgf.ImplementationFqn, StringComparison.Ordinal))
                {
                    concreteIsRegistered = true;
                    break;
                }
            }

            var currentExpr = concreteIsRegistered
                ? resolve(cgf.ImplementationFqn, false)
                : BuildClosedGenericNewExpr(cgf, resolve);

            // Chain decorators innermost-first (list is already sorted by Order ascending)
            var typeArgs = ExtractTypeArgsFromClosedFqn(cgf.InterfaceFqn);
            var closedInterfaceFqn = cgf.InterfaceFqn;
            foreach (var decorator in decorators)
            {
                var closedDecoratorFqn = CloseUnboundFqn(decorator.DecoratorFqn, typeArgs);
                var sb = new System.Text.StringBuilder();
                sb.Append("new ").Append(closedDecoratorFqn).Append("(");
                bool first = true;
                foreach (var param in decorator.ConstructorParameters)
                {
                    if (!first) sb.Append(", ");
                    first = false;
                    var closedParamType = CloseUnboundFqn(param.FullyQualifiedTypeName, typeArgs);
                    // Identify the "inner" parameter by matching the closed or unbound interface FQN
                    if (closedParamType == closedInterfaceFqn || param.FullyQualifiedTypeName == cgf.OpenServiceFqn)
                    {
                        sb.Append("(").Append(closedInterfaceFqn).Append(")(").Append(currentExpr).Append(")");
                    }
                    else
                    {
                        sb.Append(resolve(closedParamType, param.IsOptional));
                    }
                }
                sb.Append(")");
                currentExpr = sb.ToString();
            }
            return currentExpr;
        }

        /// <summary>Closes an unbound generic FQN with the supplied type args, e.g. "global::IFoo&lt;&gt;" + ["global::Bar"] → "global::IFoo&lt;global::Bar&gt;".</summary>
        private static string CloseUnboundFqn(string unboundFqn, string[] typeArgs)
        {
            var idx = unboundFqn.IndexOf('<');
            if (idx < 0) return unboundFqn; // Not generic — return as-is
            var prefix = unboundFqn.Substring(0, idx);
            return prefix + "<" + string.Join(", ", typeArgs) + ">";
        }

        /// <summary>Extracts the outermost type arguments from a closed generic FQN string.</summary>
        private static string[] ExtractTypeArgsFromClosedFqn(string closedFqn)
        {
            var openIdx = closedFqn.IndexOf('<');
            if (openIdx < 0) return new string[0];
            var closeIdx = closedFqn.LastIndexOf('>');
            if (closeIdx <= openIdx) return new string[0];
            var inner = closedFqn.Substring(openIdx + 1, closeIdx - openIdx - 1);
            var result = new System.Collections.Generic.List<string>();
            int depth = 0, start = 0;
            for (int i = 0; i < inner.Length; i++)
            {
                if (inner[i] == '<') depth++;
                else if (inner[i] == '>') depth--;
                else if (inner[i] == ',' && depth == 0)
                {
                    result.Add(inner.Substring(start, i - start).Trim());
                    start = i + 1;
                }
            }
            result.Add(inner.Substring(start).Trim());
            return result.ToArray();
        }

        private static void EmitConcreteRegistration(
            StringBuilder sb,
            string lifetime,
            string implType,
            string? key,
            bool useAdd,
            List<ConstructorParameterInfo> constructorParameters,
            List<PropertyInjectionInfo> propertyInjections)
        {
            if (key != null)
            {
                var method = useAdd ? "AddKeyed" + lifetime : "TryAddKeyed" + lifetime;
                var factory = BuildKeyedFactoryLambda(implType, constructorParameters, propertyInjections);
                var escapedKey = key.Replace("\\", "\\\\").Replace("\"", "\\\"");
                sb.AppendLine(string.Format(
                    "            services.{0}<{1}>(\"{2}\", {3});",
                    method, implType, escapedKey, factory));
            }
            else
            {
                var method = useAdd ? "Add" + lifetime : "TryAdd" + lifetime;
                var factory = BuildFactoryLambda(implType, constructorParameters, propertyInjections);
                sb.AppendLine(string.Format(
                    "            services.{0}({1});",
                    method, factory));
            }
        }

        private static DecoratorRegistrationInfo? GetDecoratorInfo(
            GeneratorAttributeSyntaxContext ctx,
            CancellationToken ct)
        {
            if (ctx.TargetSymbol is not INamedTypeSymbol typeSymbol) return null;

            var typeName = typeSymbol.Name;
            var fqn = typeSymbol.ToDisplayString(FullyQualifiedFormat);
            bool isAbstractOrStatic = typeSymbol.IsAbstract || typeSymbol.IsStatic;
            bool isOpenGeneric = typeSymbol.IsGenericType;
            int arity = typeSymbol.TypeParameters.Length;

            // For open generic decorators, convert to unbound generic form
            if (isOpenGeneric)
                fqn = ToUnboundGenericString(fqn, arity);

            // Collect all interfaces this type implements (unbound for open generics)
            var interfaces = new System.Collections.Generic.HashSet<string>();
            foreach (var iface in typeSymbol.AllInterfaces)
            {
                var ifaceFqn = iface.ToDisplayString(FullyQualifiedFormat);
                if (isOpenGeneric && iface.IsGenericType)
                    ifaceFqn = ToUnboundGenericString(ifaceFqn, arity);
                interfaces.Add(ifaceFqn);
            }

            // Find public constructor
            IMethodSymbol? ctor = null;
            foreach (var c in typeSymbol.InstanceConstructors)
            {
                if (c.DeclaredAccessibility == Accessibility.Public)
                { ctor = c; break; }
            }

            string? decoratedInterface = null;
            var ctorParams = new List<ConstructorParameterInfo>();

            if (ctor != null && !isAbstractOrStatic)
            {
                foreach (var param in ctor.Parameters)
                {
                    var paramTypeFqn = param.Type.ToDisplayString(FullyQualifiedFormat);
                    // For open generic decorators, convert param types to unbound form for matching
                    var matchFqn = (isOpenGeneric && param.Type is INamedTypeSymbol pt && pt.IsGenericType)
                        ? ToUnboundGenericString(paramTypeFqn, arity)
                        : paramTypeFqn;
                    bool isOptional = param.HasExplicitDefaultValue
                        || param.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "ZeroAlloc.Inject.OptionalDependencyAttribute");
                    ctorParams.Add(new ConstructorParameterInfo(matchFqn, param.Name, isOptional));
                    if (decoratedInterface == null && interfaces.Contains(matchFqn))
                        decoratedInterface = matchFqn;
                }
            }

            bool implementsDisposable = false;
            foreach (var iface in typeSymbol.AllInterfaces)
            {
                var name = iface.ToDisplayString();
                if (name == "System.IDisposable" || name == "System.IAsyncDisposable")
                { implementsDisposable = true; break; }
            }

            return new DecoratorRegistrationInfo(
                typeName, fqn, decoratedInterface,
                isOpenGeneric, ctorParams, implementsDisposable, isAbstractOrStatic,
                order: 0, whenRegisteredFqn: null, isDecoratorOf: false,
                location: GetClassLocation(ctx, typeSymbol),
                attributeLocation: LocationInfo.From(ctx.Attributes.FirstOrDefault()?.ApplicationSyntaxReference));
        }

        private static DecoratorRegistrationInfo? GetDecoratorOfInfo(
            GeneratorAttributeSyntaxContext ctx,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (ctx.TargetSymbol is not INamedTypeSymbol typeSymbol) return null;

            var typeName = typeSymbol.Name;
            var fqn = typeSymbol.ToDisplayString(FullyQualifiedFormat);
            bool isAbstractOrStatic = typeSymbol.IsAbstract || typeSymbol.IsStatic;
            bool isOpenGeneric = typeSymbol.IsGenericType;
            int arity = typeSymbol.TypeParameters.Length;

            if (isOpenGeneric)
                fqn = ToUnboundGenericString(fqn, arity);

            var attr = ctx.Attributes.FirstOrDefault();
            if (attr == null) return null;

            string? decoratedInterfaceFqn = null;
            if (attr.ConstructorArguments.Length > 0
                && attr.ConstructorArguments[0].Value is INamedTypeSymbol decoratedSymbol)
            {
                decoratedInterfaceFqn = decoratedSymbol.ToDisplayString(FullyQualifiedFormat);
                if (isOpenGeneric && decoratedSymbol.IsGenericType)
                    decoratedInterfaceFqn = ToUnboundGenericString(decoratedInterfaceFqn, arity);
            }

            int order = 0;
            string? whenRegisteredFqn = null;

            foreach (var named in attr.NamedArguments)
            {
                if (named.Key == "Order" && named.Value.Value is int orderVal)
                    order = orderVal;
                else if (named.Key == "WhenRegistered" && named.Value.Value is INamedTypeSymbol whenSymbol)
                    whenRegisteredFqn = whenSymbol.ToDisplayString(FullyQualifiedFormat);
            }

            // Collect interfaces to validate — null decoratedInterfaceFqn signals ZI016 in RegisterSourceOutput
            var interfaces = new System.Collections.Generic.HashSet<string>();
            foreach (var iface in typeSymbol.AllInterfaces)
            {
                var ifaceFqn = iface.ToDisplayString(FullyQualifiedFormat);
                if (isOpenGeneric && iface.IsGenericType)
                    ifaceFqn = ToUnboundGenericString(ifaceFqn, arity);
                interfaces.Add(ifaceFqn);
            }

            if (decoratedInterfaceFqn != null && !interfaces.Contains(decoratedInterfaceFqn))
                decoratedInterfaceFqn = null;

            // Build constructor params
            IMethodSymbol? ctor = null;
            foreach (var c in typeSymbol.InstanceConstructors)
            {
                if (c.DeclaredAccessibility == Accessibility.Public) { ctor = c; break; }
            }

            var ctorParams = new List<ConstructorParameterInfo>();
            if (ctor != null && !isAbstractOrStatic)
            {
                foreach (var param in ctor.Parameters)
                {
                    var paramTypeFqn = param.Type.ToDisplayString(FullyQualifiedFormat);
                    var matchFqn = (isOpenGeneric && param.Type is INamedTypeSymbol pt && pt.IsGenericType)
                        ? ToUnboundGenericString(paramTypeFqn, arity)
                        : paramTypeFqn;
                    var paramAttrs = param.GetAttributes();
                    bool isOptional = param.HasExplicitDefaultValue
                        || paramAttrs.Any(a => a.AttributeClass?.ToDisplayString() == "ZeroAlloc.Inject.OptionalDependencyAttribute");
                    ctorParams.Add(new ConstructorParameterInfo(matchFqn, param.Name, isOptional));
                }
            }

            bool implementsDisposable = false;
            foreach (var iface in typeSymbol.AllInterfaces)
            {
                var name = iface.ToDisplayString();
                if (name == "System.IDisposable" || name == "System.IAsyncDisposable")
                { implementsDisposable = true; break; }
            }

            return new DecoratorRegistrationInfo(
                typeName, fqn, decoratedInterfaceFqn, isOpenGeneric, ctorParams,
                implementsDisposable, isAbstractOrStatic,
                order: order, whenRegisteredFqn: whenRegisteredFqn, isDecoratorOf: true,
                location: GetClassLocation(ctx, typeSymbol),
                attributeLocation: LocationInfo.From(attr.ApplicationSyntaxReference));
        }

        /// <summary>Whether a constructor parameter is resolved optionally: it has a default value or [OptionalDependency].</summary>
        private static bool IsOptionalParameter(IParameterSymbol parameter) =>
            parameter.HasExplicitDefaultValue
            || parameter.GetAttributes().Any(static a => a.AttributeClass?.ToDisplayString() == "ZeroAlloc.Inject.OptionalDependencyAttribute");

        private static ImmutableArray<ClosedGenericFactoryInfo> FindClosedGenericUsages(
            ((((ImmutableArray<ServiceRegistrationInfo?> transients,
                ImmutableArray<ServiceRegistrationInfo?> scopeds),
               ImmutableArray<ServiceRegistrationInfo?> singletons),
              ImmutableArray<DecoratorRegistrationInfo?> decorators),
             Compilation compilation) data,
            CancellationToken ct)
        {
            var transients  = data.Item1.Item1.Item1.Item1;
            var scopeds     = data.Item1.Item1.Item1.Item2;
            var singletons  = data.Item1.Item1.Item2;
            var decorators  = data.Item1.Item2;
            var compilation = data.Item2;

            // Unbound service type (global::IFoo<,> form) → the open generic registrations the generated
            // Add...Services extension makes for it, by key, each in order. It registers transients, then
            // scopeds, then singletons, with TryAdd, which skips a service type and key that is already
            // registered, or with Add when AllowMultiple is set. Microsoft DI resolves the last
            // registration. Without As, the implementation type is registered as a service type too.
            var openGenericMap = new Dictionary<string, List<KeyedRegistrations>>(StringComparer.Ordinal);
            var registrable = new List<ServiceRegistrationInfo>();
            AddRegistrable(registrable, transients, null, null);
            AddRegistrable(registrable, scopeds, null, null);
            AddRegistrable(registrable, singletons, null, null);
            foreach (var svc in registrable)
            {
                if (!svc.IsOpenGeneric || svc.ImplementationMetadataName == null) continue;
                foreach (var serviceType in GetServiceTypes(svc))
                {
                    if (!openGenericMap.TryGetValue(serviceType, out var byKey))
                    {
                        byKey = new List<KeyedRegistrations>();
                        openGenericMap[serviceType] = byKey;
                    }
                    var group = byKey.Find(g => string.Equals(g.Key, svc.Key, StringComparison.Ordinal));
                    if (group == null)
                    {
                        group = new KeyedRegistrations(svc.Key);
                        byKey.Add(group);
                    }
                    if (svc.AllowMultiple || group.Registrations.Count == 0)
                        group.Registrations.Add(svc);
                }
            }

            if (openGenericMap.Count == 0) return ImmutableArray<ClosedGenericFactoryInfo>.Empty;

            // A decorated closed form wraps the concrete registration of its implementation, as the
            // non-generic decorators do, so the concrete closed form is needed wherever it is.
            var decoratedServiceTypes = GroupDecorators(ValidDecorators(registrable, decorators, null)).Keys;

            // Seed work queue from all constructor parameters of all registered services
            var workQueue = new Queue<ConstructorParameterInfo>();
            var processed = new HashSet<string>(StringComparer.Ordinal); // keyed by closed interface FQN
            var results = new List<ClosedGenericFactoryInfo>();

            foreach (var svc in registrable)
            {
                foreach (var param in svc.ConstructorParameters)
                    if (param.UnboundGenericInterfaceFqn != null)
                        workQueue.Enqueue(param);
            }

            while (workQueue.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                var param = workQueue.Dequeue();
                var closedFqn = param.FullyQualifiedTypeName;

                if (!processed.Add(closedFqn)) continue;
                if (param.UnboundGenericInterfaceFqn == null) continue;
                if (!openGenericMap.TryGetValue(param.UnboundGenericInterfaceFqn, out var byKey)) continue;

                var typeArgSymbols = new ITypeSymbol[param.TypeArgumentReferenceIds.Length];
                bool allResolved = true;
                for (int i = 0; i < param.TypeArgumentReferenceIds.Length; i++)
                {
                    var sym = DocumentationCommentId.GetFirstSymbolForReferenceId(
                        param.TypeArgumentReferenceIds[i], compilation) as ITypeSymbol;
                    // A parameter of an open generic service, such as IContext<T> on Repository<T>,
                    // is not a closed usage; its closed forms are found through the closed services.
                    if (sym == null || ContainsTypeParameter(sym)) { allResolved = false; break; }
                    typeArgSymbols[i] = sym;
                }
                if (!allResolved) continue;

                bool hasValueTypeArgument = typeArgSymbols.Any(static t => t.IsValueType);

                foreach (var group in byKey)
                {
                    var registrations = group.Registrations;
                    var closedForm = new List<ClosedGenericFactoryInfo>(registrations.Count);
                    var concreteUsages = new List<ConstructorParameterInfo>();
                    for (int r = 0; r < registrations.Count; r++)
                    {
                        var og = registrations[r];

                        // Resolve impl symbol and close it
                        var implSymbol = compilation.GetTypeByMetadataName(og.ImplementationMetadataName!);
                        if (implSymbol == null) continue;

                        var closedImpl = implSymbol.Construct(typeArgSymbols);
                        var closedImplFqn = closedImpl.ToDisplayString(FullyQualifiedFormat);

                        bool implementsDisposable = closedImpl.AllInterfaces.Any(static i =>
                            i.SpecialType == SpecialType.System_IDisposable
                            || string.Equals(i.ToDisplayString(), "System.IAsyncDisposable", StringComparison.Ordinal));

                        // Build constructor parameters for the closed implementation (type args substituted by Roslyn)
                        var ctor = closedImpl.InstanceConstructors
                            .Where(static c => c.DeclaredAccessibility == Accessibility.Public)
                            .OrderByDescending(static c => c.Parameters.Length)
                            .FirstOrDefault();
                        if (ctor == null) continue;

                        var ctorParams = ImmutableArray.CreateBuilder<ConstructorParameterInfo>(ctor.Parameters.Length);
                        foreach (var ctorParam in ctor.Parameters)
                        {
                            var ctorParamFqn = ctorParam.Type.ToDisplayString(FullyQualifiedFormat);
                            string? ctorUnboundFqn = null;
                            ImmutableArray<string> ctorTypeArgReferenceIds = ImmutableArray<string>.Empty;

                            if (ctorParam.Type is INamedTypeSymbol namedCtorParam
                                && namedCtorParam.IsGenericType && !namedCtorParam.IsUnboundGenericType)
                            {
                                var rawUnbound = namedCtorParam.ConstructedFrom.ToDisplayString(FullyQualifiedFormat);
                                ctorUnboundFqn = ToUnboundGenericString(rawUnbound, namedCtorParam.TypeArguments.Length);
                                ctorTypeArgReferenceIds = ToTypeArgumentReferenceIds(namedCtorParam);
                            }

                            var ctorParamInfo = new ConstructorParameterInfo(
                                ctorParamFqn, ctorParam.Name, IsOptionalParameter(ctorParam), ctorUnboundFqn, ctorTypeArgReferenceIds);
                            // Add to work queue for fixed-point iteration if it is a closed generic
                            if (ctorUnboundFqn != null)
                                workQueue.Enqueue(ctorParamInfo);
                            ctorParams.Add(ctorParamInfo);
                        }

                        closedForm.Add(new ClosedGenericFactoryInfo(
                            closedFqn,
                            closedImplFqn,
                            param.UnboundGenericInterfaceFqn,
                            group.Key,
                            og.Lifetime,
                            ctorParams.ToImmutable(),
                            implementsDisposable,
                            isResolved: r == registrations.Count - 1,
                            hasValueTypeArgument));

                        // A decorated closed form wraps the concrete closed form, so find that one too.
                        if (group.Key == null && og.AsType == null && decoratedServiceTypes.Contains(param.UnboundGenericInterfaceFqn))
                        {
                            concreteUsages.Add(new ConstructorParameterInfo(
                                closedImplFqn, "inner", false, og.FullyQualifiedName, ToTypeArgumentReferenceIds(closedImpl)));
                        }
                    }

                    // Every registration of the closed form has to close, or none is used: resolving it
                    // would otherwise return a registration Microsoft DI does not.
                    if (closedForm.Count == registrations.Count)
                    {
                        results.AddRange(closedForm);
                        foreach (var usage in concreteUsages)
                            workQueue.Enqueue(usage);
                    }
                }
            }

            return results.ToImmutableArray();
        }
    }

    /// <summary>The open generic registrations of one service type under one key, in order.</summary>
    internal sealed class KeyedRegistrations
    {
        public KeyedRegistrations(string? key) => Key = key;

        public string? Key { get; }

        public List<ServiceRegistrationInfo> Registrations { get; } = new List<ServiceRegistrationInfo>();
    }

    /// <summary>
    /// An unkeyed non-generic service type that has decorators, of one service registration. The
    /// generated containers emit a factory for each, and a cache for a singleton or scoped one.
    /// </summary>
    internal sealed class DecoratedServiceEntry
    {
        public ServiceRegistrationInfo Svc { get; }
        public string ServiceType { get; }
        public List<DecoratorRegistrationInfo> Decorators { get; }
        public int Index { get; }

        public DecoratedServiceEntry(ServiceRegistrationInfo svc, string serviceType, List<DecoratorRegistrationInfo> decorators, int index)
        {
            Svc = svc;
            ServiceType = serviceType;
            Decorators = decorators;
            Index = index;
        }
    }

    internal sealed class ServiceTypeGroupEntry
    {
        public ServiceRegistrationInfo Svc { get; }
        public string Lifetime { get; }
        public int FieldIndex { get; }

        public ServiceTypeGroupEntry(ServiceRegistrationInfo svc, string lifetime, int fieldIndex)
        {
            Svc = svc;
            Lifetime = lifetime;
            FieldIndex = fieldIndex;
        }
    }
}
