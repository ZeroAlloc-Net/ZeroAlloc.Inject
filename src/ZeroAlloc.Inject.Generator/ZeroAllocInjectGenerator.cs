#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;

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
                .Collect();

            var scopeds = context.SyntaxProvider.ForAttributeWithMetadataName(
                "ZeroAlloc.Inject.ScopedAttribute",
                predicate: static (node, _) => true,
                transform: static (ctx, ct) => GetServiceInfo(ctx, "Scoped", ct))
                .Where(static x => x != null)
                .Collect();

            var singletons = context.SyntaxProvider.ForAttributeWithMetadataName(
                "ZeroAlloc.Inject.SingletonAttribute",
                predicate: static (node, _) => true,
                transform: static (ctx, ct) => GetServiceInfo(ctx, "Singleton", ct))
                .Where(static x => x != null)
                .Collect();

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

            var decorators = context.SyntaxProvider.ForAttributeWithMetadataName(
                "ZeroAlloc.Inject.DecoratorAttribute",
                predicate: static (node, _) => true,
                transform: static (ctx, ct) => GetDecoratorInfo(ctx, ct))
                .Where(static x => x != null)
                .Collect();

            var decoratorOfs = context.SyntaxProvider.ForAttributeWithMetadataName(
                "ZeroAlloc.Inject.DecoratorOfAttribute",
                predicate: static (node, _) => true,
                transform: static (ctx, ct) => GetDecoratorOfInfo(ctx, ct))
                .Where(static x => x != null)
                .Collect();

            var allDecorators = decorators.Combine(decoratorOfs)
                .Select(static (pair, _) =>
                {
                    var builder = ImmutableArray.CreateBuilder<DecoratorRegistrationInfo?>();
                    builder.AddRange(pair.Left);
                    builder.AddRange(pair.Right);
                    return builder.ToImmutable();
                });

            var closedGenericUsages = transients
                .Combine(scopeds)
                .Combine(singletons)
                .Combine(context.CompilationProvider)
                .Select(static (data, ct) => FindClosedGenericUsages(data, ct));

            var combined = transients
                .Combine(scopeds)
                .Combine(singletons)
                .Combine(assemblyAttr)
                .Combine(assemblyName)
                .Combine(hasContainer)
                .Combine(allDecorators)
                .Combine(closedGenericUsages);

            context.RegisterSourceOutput(combined, static (spc, data) =>
            {
                var closedGenericFactories = data.Right;  // NEW
                var transientInfos = data.Left.Left.Left.Left.Left.Left.Left;
                var scopedInfos    = data.Left.Left.Left.Left.Left.Left.Right;
                var singletonInfos = data.Left.Left.Left.Left.Left.Right;
                var methodNameOverrides = data.Left.Left.Left.Left.Right;
                var asmName        = data.Left.Left.Left.Right;
                var containerReferenced = data.Left.Left.Right;
                var decoratorInfos = data.Left.Right;

                var allServices = new List<ServiceRegistrationInfo>();
                AddNonNull(allServices, transientInfos);
                AddNonNull(allServices, scopedInfos);
                AddNonNull(allServices, singletonInfos);

                // Report diagnostics
                foreach (var svc in allServices)
                {
                    if (!svc.HasPublicConstructor)
                    {
                        spc.ReportDiagnostic(Diagnostic.Create(
                            DiagnosticDescriptors.NoPublicConstructor,
                            Location.None,
                            svc.TypeName));
                    }

                    if (svc.Interfaces.Count == 0 && svc.AsType == null)
                    {
                        spc.ReportDiagnostic(Diagnostic.Create(
                            DiagnosticDescriptors.NoInterfaces,
                            Location.None,
                            svc.TypeName));
                    }

                    if (svc.HasMultipleConstructors)
                    {
                        spc.ReportDiagnostic(Diagnostic.Create(
                            DiagnosticDescriptors.MultipleConstructorsNoAttribute,
                            Location.None,
                            svc.TypeName));
                    }

                    if (svc.PrimitiveParameterName != null)
                    {
                        spc.ReportDiagnostic(Diagnostic.Create(
                            DiagnosticDescriptors.PrimitiveConstructorParameter,
                            Location.None,
                            svc.PrimitiveParameterName,
                            svc.TypeName,
                            svc.PrimitiveParameterType));
                    }

                    if (svc.OptionalNonNullableParamName != null)
                    {
                        spc.ReportDiagnostic(Diagnostic.Create(
                            DiagnosticDescriptors.OptionalDependencyOnNonNullable,
                            Location.None,
                            svc.OptionalNonNullableParamName,
                            svc.TypeName,
                            svc.OptionalNonNullableParamType));
                    }
                }

                if (allServices.Count == 0 && decoratorInfos.Length == 0)
                {
                    return;
                }

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
                        spc.ReportDiagnostic(Diagnostic.Create(
                            DiagnosticDescriptors.DecoratorOnAbstractOrStatic,
                            Location.None, dec.TypeName));
                        continue;
                    }
                    if (dec.DecoratedInterfaceFqn == null)
                    {
                        if (dec.IsDecoratorOf)
                        {
                            spc.ReportDiagnostic(Diagnostic.Create(
                                DiagnosticDescriptors.DecoratorOfInterfaceNotImplemented,
                                Location.None, dec.TypeName, dec.DecoratorFqn));
                        }
                        else
                        {
                            spc.ReportDiagnostic(Diagnostic.Create(
                                DiagnosticDescriptors.DecoratorNoMatchingInterface,
                                Location.None, dec.TypeName));
                        }
                        continue;
                    }
                    if (!registeredInterfaces.Contains(dec.DecoratedInterfaceFqn))
                    {
                        spc.ReportDiagnostic(Diagnostic.Create(
                            DiagnosticDescriptors.DecoratorNoRegisteredInner,
                            Location.None, dec.TypeName, dec.DecoratedInterfaceFqn));
                        continue;
                    }
                    validDecorators.Add(dec);
                }

                // Build dictionary: decorated interface FQN → list of decorator infos
                var decoratorsByInterface = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<DecoratorRegistrationInfo>>();
                foreach (var dec in validDecorators)
                {
                    if (!decoratorsByInterface.TryGetValue(dec.DecoratedInterfaceFqn!, out var list))
                    {
                        list = new System.Collections.Generic.List<DecoratorRegistrationInfo>();
                        decoratorsByInterface[dec.DecoratedInterfaceFqn!] = list;
                    }
                    list.Add(dec);
                }

                // Sort each decorator list by Order ascending, and check for ZI017 (duplicate Order)
                foreach (var kvp in decoratorsByInterface)
                {
                    var list = kvp.Value;
                    list.Sort(static (a, b) => a.Order.CompareTo(b.Order));

                    for (int i = 0; i < list.Count - 1; i++)
                    {
                        if (list[i].IsDecoratorOf && list[i + 1].IsDecoratorOf && list[i].Order == list[i + 1].Order)
                        {
                            spc.ReportDiagnostic(Diagnostic.Create(
                                DiagnosticDescriptors.DecoratorOfDuplicateOrder,
                                Location.None,
                                kvp.Key,
                                list[i].Order.ToString(),
                                list[i].TypeName,
                                list[i + 1].TypeName));
                        }
                    }
                }

                DetectCircularDependencies(spc, allServices, decoratorsByInterface);

                // ZI018: warn when an open generic has no detected closed usages
                {
                    var closedFqnSet = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
                    foreach (var cgf in closedGenericFactories)
                        closedFqnSet.Add(cgf.InterfaceFqn);

                    foreach (var svc in allServices)
                    {
                        if (!svc.IsOpenGeneric) continue;

                        var ifaces = svc.AsType != null
                            ? new System.Collections.Generic.List<string> { svc.AsType }
                            : svc.Interfaces;

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
                            spc.ReportDiagnostic(Diagnostic.Create(
                                DiagnosticDescriptors.NoDetectedClosedUsages,
                                Location.None,
                                svc.TypeName));
                        }
                    }
                }

                if (allServices.Count == 0)
                {
                    return;
                }

                string? methodNameOverride = null;
                if (methodNameOverrides.Length > 0)
                {
                    methodNameOverride = methodNameOverrides[0];
                }

                var source = GenerateExtensionClass(allServices, asmName, methodNameOverride, decoratorsByInterface);
                spc.AddSource("ZeroAlloc.Inject.ServiceCollectionExtensions.g.cs", source);

                if (containerReferenced)
                {
                    var providerSource = GenerateServiceProviderClass(allServices, asmName, decoratorsByInterface);
                    spc.AddSource("ZeroAlloc.Inject.ServiceProvider.g.cs", providerSource);

                    var standaloneCode = GenerateStandaloneServiceProviderClass(allServices, asmName, decoratorsByInterface, closedGenericFactories);
                    spc.AddSource(asmName + ".StandaloneServiceProvider.g.cs", standaloneCode);
                }
            });
        }

        private static void AddNonNull(List<ServiceRegistrationInfo> list, ImmutableArray<ServiceRegistrationInfo?> items)
        {
            foreach (var item in items)
            {
                if (item != null)
                {
                    list.Add(item);
                }
            }
        }

        /// <summary>
        /// Converts a fully qualified generic type string like "global::Ns.Foo&lt;T, U&gt;" to
        /// the unbound generic form "global::Ns.Foo&lt;,&gt;" suitable for use in typeof() expressions.
        /// </summary>
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

            if (typeSymbol.IsAbstract || typeSymbol.IsStatic)
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

            // Extract attribute properties
            string? asType = null;
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
                    }

                    string? unboundFqn = null;
                    ImmutableArray<string> typeArgMetadataNames = ImmutableArray<string>.Empty;
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
                        var taBuilder = ImmutableArray.CreateBuilder<string>(namedParam.TypeArguments.Length);
                        foreach (var typeArg in namedParam.TypeArguments)
                        {
                            var typeArgNs = typeArg.ContainingNamespace is { IsGlobalNamespace: false } nns
                                ? nns.ToDisplayString()
                                : null;
                            taBuilder.Add(typeArgNs != null ? typeArgNs + "." + typeArg.MetadataName : typeArg.MetadataName);
                        }
                        typeArgMetadataNames = taBuilder.ToImmutable();
                    }

                    constructorParameters.Add(new ConstructorParameterInfo(
                        paramTypeFqn,
                        param.Name,
                        isOptional,
                        unboundFqn,
                        typeArgMetadataNames));

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
                        }
                    }
                }
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
                implementationMetadataName);
        }

        private static string GenerateExtensionClass(
            List<ServiceRegistrationInfo> services,
            string assemblyName,
            string? methodNameOverride,
            System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<DecoratorRegistrationInfo>> decoratorsByInterface)
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
            sb.AppendLine("#nullable enable");
            sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
            sb.AppendLine("using Microsoft.Extensions.DependencyInjection.Extensions;");
            if (hasConditionalDecorators)
                sb.AppendLine("using System.Linq;");
            sb.AppendLine();
            sb.AppendLine("namespace Microsoft.Extensions.DependencyInjection");
            sb.AppendLine("{");
            sb.AppendLine("    public static class " + className);
            sb.AppendLine("    {");
            sb.AppendLine("        public static IServiceCollection " + methodName + "(this IServiceCollection services)");
            sb.AppendLine("        {");

            foreach (var svc in services)
            {
                EmitRegistration(sb, svc, decoratorsByInterface);
            }

            sb.AppendLine("            return services;");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            return sb.ToString();
        }

        private static string BuildFactoryLambda(string implType, List<ConstructorParameterInfo> parameters)
        {
            return BuildFactoryLambdaCore(implType, parameters, false);
        }

        private static string BuildKeyedFactoryLambda(string implType, List<ConstructorParameterInfo> parameters)
        {
            return BuildFactoryLambdaCore(implType, parameters, true);
        }

        private static string BuildFactoryLambdaCore(string implType, List<ConstructorParameterInfo> parameters, bool keyed)
        {
            var spPrefix = keyed ? "(sp, _) => new " : "sp => new ";

            if (parameters.Count == 0)
            {
                return spPrefix + implType + "()";
            }

            var factorySb = new StringBuilder();
            factorySb.Append(spPrefix);
            factorySb.Append(implType);
            factorySb.Append("(\n");

            for (int i = 0; i < parameters.Count; i++)
            {
                var param = parameters[i];
                var method = param.IsOptional ? "GetService" : "GetRequiredService";
                factorySb.Append("                sp.");
                factorySb.Append(method);
                factorySb.Append("<");
                factorySb.Append(param.FullyQualifiedTypeName);
                factorySb.Append(">()");
                if (i < parameters.Count - 1)
                {
                    factorySb.Append(",");
                }
                factorySb.Append("\n");
            }

            factorySb.Append("            )");
            return factorySb.ToString();
        }

        private static void EmitRegistration(
            StringBuilder sb,
            ServiceRegistrationInfo svc,
            System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<DecoratorRegistrationInfo>> decoratorsByInterface)
        {
            var lifetime = svc.Lifetime;
            var fqn = svc.FullyQualifiedName;
            var useAdd = svc.AllowMultiple;

            if (svc.AsType != null)
            {
                // Only register as the specified type
                EmitSingleRegistration(sb, lifetime, svc.AsType, fqn, svc.Key, useAdd, svc.IsOpenGeneric, svc.ConstructorParameters);
                return;
            }

            // Register all non-filtered interfaces, wrapping with decorator when applicable
            foreach (var iface in svc.Interfaces)
            {
                if (!svc.IsOpenGeneric && decoratorsByInterface.TryGetValue(iface, out var decorators))
                {
                    // Emit factory wrapping with chained decorators, applying WhenRegistered guards per decorator
                    EmitDecoratorRegistrations(sb, lifetime, iface, fqn, decorators);
                }
                else
                {
                    EmitSingleRegistration(sb, lifetime, iface, fqn, svc.Key, useAdd, svc.IsOpenGeneric, svc.ConstructorParameters);
                }
            }

            // Always register concrete type (inner needs to be resolvable by itself)
            EmitConcreteRegistration(sb, lifetime, fqn, svc.Key, useAdd, svc.IsOpenGeneric, svc.ConstructorParameters);
        }

        private static void EmitDecoratorRegistrations(
            StringBuilder sb,
            string lifetime,
            string ifaceFqn,
            string innerConcreteFqn,
            List<DecoratorRegistrationInfo> decorators)
        {
            // Check if any decorator has a WhenRegistered guard
            bool hasAnyConditional = false;
            foreach (var dec in decorators)
            {
                if (dec.WhenRegisteredFqn != null)
                {
                    hasAnyConditional = true;
                    break;
                }
            }

            if (!hasAnyConditional)
            {
                // Fast path: no conditional decorators, emit the full chain as a single registration
                var decoratorFactory = BuildDecoratorFactoryLambdaChained(decorators, innerConcreteFqn);
                sb.AppendLine(string.Format(
                    "            services.Add{0}<{1}>({2});",
                    lifetime, ifaceFqn, decoratorFactory));
                return;
            }

            // Slow path: at least one conditional decorator — emit per-decorator registrations
            // We build the chain incrementally. Unconditional decorators up to each conditional one
            // are folded into the chain expression before the conditional check.
            var currentInner = innerConcreteFqn;
            // Split decorators into groups: run of unconditional, then a conditional
            // For simplicity, process each decorator individually, accumulating the chain
            var unconditionalAccumulator = new System.Collections.Generic.List<DecoratorRegistrationInfo>();

            foreach (var dec in decorators)
            {
                if (dec.WhenRegisteredFqn == null)
                {
                    // Unconditional: accumulate for later chain building
                    unconditionalAccumulator.Add(dec);
                }
                else
                {
                    // Conditional decorator: first flush unconditional ones if any, then emit conditional
                    // Build the chain so far with unconditional + this conditional decorator
                    var chainDecorators = new System.Collections.Generic.List<DecoratorRegistrationInfo>(unconditionalAccumulator) { dec };
                    var decoratorFactory = BuildDecoratorFactoryLambdaChained(chainDecorators, currentInner);

                    sb.AppendLine(string.Format(
                        "            if (services.Any(d => d.ServiceType == typeof({0})))",
                        dec.WhenRegisteredFqn));
                    sb.AppendLine("            {");
                    sb.AppendLine(string.Format(
                        "                services.Add{0}<{1}>({2});",
                        lifetime, ifaceFqn, decoratorFactory));
                    sb.AppendLine("            }");

                    // After a conditional registration, the chain is emitted inside the guard.
                    // For subsequent decorators, they would wrap the result of this conditional.
                    // Since we cannot know at generation time whether the conditional registration
                    // ran, we reset the accumulator and continue chaining on the same base.
                    unconditionalAccumulator.Clear();
                    // Note: currentInner stays the same — subsequent decorators wrap from the concrete
                }
            }

            // If there are remaining unconditional decorators that were never followed by a conditional,
            // emit them as a plain registration
            if (unconditionalAccumulator.Count > 0)
            {
                var decoratorFactory = BuildDecoratorFactoryLambdaChained(unconditionalAccumulator, currentInner);
                sb.AppendLine(string.Format(
                    "            services.Add{0}<{1}>({2});",
                    lifetime, ifaceFqn, decoratorFactory));
            }
        }

        private static string BuildDecoratorFactoryLambdaChained(
            List<DecoratorRegistrationInfo> decorators,
            string innerConcreteFqn)
        {
            // Build the innermost expression: sp.GetRequiredService<ConcreteType>()
            var currentExpr = "sp.GetRequiredService<" + innerConcreteFqn + ">()";

            // Chain each decorator: first wraps concrete, each subsequent wraps previous
            foreach (var decorator in decorators)
            {
                var sb = new StringBuilder();
                sb.Append("new ");
                sb.Append(decorator.DecoratorFqn);
                sb.Append("(");
                bool first = true;
                foreach (var param in decorator.ConstructorParameters)
                {
                    if (!first) sb.Append(", ");
                    first = false;
                    if (param.FullyQualifiedTypeName == decorator.DecoratedInterfaceFqn)
                    {
                        sb.Append(currentExpr);
                    }
                    else
                    {
                        var method = param.IsOptional ? "GetService" : "GetRequiredService";
                        sb.Append("sp.").Append(method).Append("<").Append(param.FullyQualifiedTypeName).Append(">()");
                    }
                }
                sb.Append(")");
                currentExpr = sb.ToString();
            }

            return "sp => " + currentExpr;
        }

        private static void EmitSingleRegistration(
            StringBuilder sb,
            string lifetime,
            string serviceType,
            string implType,
            string? key,
            bool useAdd,
            bool isOpenGeneric,
            List<ConstructorParameterInfo> constructorParameters)
        {
            if (isOpenGeneric)
            {
                // For open generics, use ServiceDescriptor
                var addOrTryAdd = useAdd ? "Add" : "TryAdd";
                sb.AppendLine(string.Format(
                    "            services.{0}(ServiceDescriptor.{1}(typeof({2}), typeof({3})));",
                    addOrTryAdd, lifetime, serviceType, implType));
                return;
            }

            if (key != null)
            {
                var method = useAdd ? "AddKeyed" + lifetime : "TryAddKeyed" + lifetime;
                var factory = BuildKeyedFactoryLambda(implType, constructorParameters);
                var escapedKey = key.Replace("\\", "\\\\").Replace("\"", "\\\"");
                sb.AppendLine(string.Format(
                    "            services.{0}<{1}>(\"{2}\", {3});",
                    method, serviceType, escapedKey, factory));
            }
            else
            {
                var method = useAdd ? "Add" + lifetime : "TryAdd" + lifetime;
                var factory = BuildFactoryLambda(implType, constructorParameters);
                sb.AppendLine(string.Format(
                    "            services.{0}<{1}>({2});",
                    method, serviceType, factory));
            }
        }

        private static string BuildDecoratedNewExpression(
            ServiceRegistrationInfo svc,
            string serviceTypeFqn,
            System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<DecoratorRegistrationInfo>> decoratorsByInterface,
            bool forScope)
        {
            var baseExpr = forScope ? BuildNewExpressionForScope(svc) : BuildNewExpression(svc);
            if (!decoratorsByInterface.TryGetValue(serviceTypeFqn, out var decorators))
                return baseExpr;

            // Chain decorators: first wraps concrete, each subsequent wraps previous
            var currentExpr = baseExpr;
            foreach (var decorator in decorators)
            {
                currentExpr = BuildNewExpressionWithDecorator(
                    decorator, svc.FullyQualifiedName, currentExpr, decorator.DecoratedInterfaceFqn!);
            }
            return currentExpr;
        }

        private static string BuildNewExpressionWithDecorator(
            DecoratorRegistrationInfo decorator,
            string innerConcreteFqn,
            string innerNewExpr,
            string decoratedInterfaceFqn)
        {
            var sb = new StringBuilder();
            sb.Append("new ").Append(decorator.DecoratorFqn).Append("(");
            bool first = true;
            foreach (var param in decorator.ConstructorParameters)
            {
                if (!first) sb.Append(", ");
                first = false;
                if (param.FullyQualifiedTypeName == decoratedInterfaceFqn)
                {
                    sb.Append("(").Append(decoratedInterfaceFqn).Append(")(").Append(innerNewExpr).Append(")");
                }
                else
                {
                    sb.Append("(").Append(param.FullyQualifiedTypeName).Append(")GetService(typeof(").Append(param.FullyQualifiedTypeName).Append("))!");
                }
            }
            sb.Append(")");
            return sb.ToString();
        }

        private static string GenerateServiceProviderClass(
            List<ServiceRegistrationInfo> services,
            string assemblyName,
            System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<DecoratorRegistrationInfo>> decoratorsByInterface)
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

            bool hasKeyedServices = keyedServices.Count > 0;

            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated />");
            sb.AppendLine("#nullable enable");
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Threading;");
            sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
            sb.AppendLine();
            sb.AppendLine("namespace ZeroAlloc.Inject.Generated");
            sb.AppendLine("{");
            var baseClass = "global::ZeroAlloc.Inject.Container.ZeroAllocInjectServiceProviderBase";
            if (hasKeyedServices)
            {
                baseClass = baseClass + ", IKeyedServiceProvider";
            }
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
                sb.AppendLine("        private " + singletons[i].FullyQualifiedName + "? _singleton_" + i + ";");
            }
            // Keyed singleton fields
            for (int i = 0; i < keyedSingletons.Count; i++)
            {
                sb.AppendLine("        private " + keyedSingletons[i].FullyQualifiedName + "? _keyedSingleton_" + i + ";");
            }
            if (singletons.Count > 0 || keyedSingletons.Count > 0)
            {
                sb.AppendLine();
            }

            // Constructor
            sb.AppendLine("        public " + className + "(IServiceProvider fallback) : base(fallback) { }");
            sb.AppendLine();

            // ResolveKnown - root provider: transients + singletons (scoped returns null)
            sb.AppendLine("        protected override object? ResolveKnown(Type serviceType)");
            sb.AppendLine("        {");

            if (hasKeyedServices)
            {
                sb.AppendLine("            if (serviceType == typeof(IKeyedServiceProvider))");
                sb.AppendLine("                return this;");
            }

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
                        var newExpr = BuildDecoratedNewExpression(svc, serviceType, decoratorsByInterface, false);
                        sb.AppendLine("            if (serviceType == typeof(" + serviceType + "))");
                        sb.AppendLine("                return " + newExpr + ";");
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
                    sb.AppendLine("            {");
                    sb.AppendLine("                if (" + fieldName + " != null) return " + fieldName + ";");
                    sb.AppendLine("                var instance = " + newExpr + ";");
                    if (svc.ImplementsDisposable)
                    {
                        sb.AppendLine("                var existing = Interlocked.CompareExchange(ref " + fieldName + ", instance, null);");
                        sb.AppendLine("                if (existing != null) { (instance as System.IDisposable)?.Dispose(); return existing; }");
                        sb.AppendLine("                return " + fieldName + ";");
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
                sb.Append("                return new " + serviceType + "[] { ");

                for (int j = 0; j < rootEntries.Count; j++)
                {
                    if (j > 0) sb.Append(", ");
                    var entry = rootEntries[j];

                    if (entry.Lifetime == "Transient")
                    {
                        sb.Append(BuildNewExpression(entry.Svc));
                    }
                    else if (entry.Lifetime == "Singleton")
                    {
                        // Use concrete type to resolve — avoids last-wins returning same instance for all
                        sb.Append("(" + serviceType + ")GetService(typeof(" + entry.Svc.FullyQualifiedName + "))!");
                    }
                }

                sb.AppendLine(" };");
                sb.AppendLine("            }");
            }

            sb.AppendLine("            return null;");
            sb.AppendLine("        }");
            sb.AppendLine();

            EmitIsKnownService(sb, serviceTypeGroups, hasKeyedServices);
            sb.AppendLine();

            EmitIsKnownKeyedService(sb, keyedServices);
            sb.AppendLine();

            // Keyed service methods
            if (hasKeyedServices)
            {
                sb.AppendLine("        public object? GetKeyedService(Type serviceType, object? serviceKey)");
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
                        if (svc.ImplementsDisposable)
                        {
                            sb.AppendLine("                    var existing = Interlocked.CompareExchange(ref " + fieldName + ", instance, null);");
                            sb.AppendLine("                    if (existing != null) { (instance as System.IDisposable)?.Dispose(); return existing; }");
                            sb.AppendLine("                    return " + fieldName + ";");
                        }
                        else
                        {
                            sb.AppendLine("                    return Interlocked.CompareExchange(ref " + fieldName + ", instance, null) ?? " + fieldName + ";");
                        }
                        sb.AppendLine("                }");
                    }
                }

                // Keyed transients - new instance each call
                foreach (var svc in keyedTransients)
                {
                    var serviceTypes = GetServiceTypes(svc);
                    var newExpr = BuildNewExpression(svc);
                    var escapedKey = svc.Key!.Replace("\\", "\\\\").Replace("\"", "\\\"");
                    foreach (var serviceType in serviceTypes)
                    {
                        sb.AppendLine("                if (serviceType == typeof(" + serviceType + ") && key == \"" + escapedKey + "\")");
                        sb.AppendLine("                    return " + newExpr + ";");
                    }
                }

                sb.AppendLine("            }");
                sb.AppendLine("            return null;");
                sb.AppendLine("        }");
                sb.AppendLine();
                sb.AppendLine("        public object GetRequiredKeyedService(Type serviceType, object? serviceKey)");
                sb.AppendLine("        {");
                sb.AppendLine("            var result = GetKeyedService(serviceType, serviceKey);");
                sb.AppendLine("            if (result == null) throw new InvalidOperationException($\"No keyed service of type '{serviceType}' with key '{serviceKey}' has been registered.\");");
                sb.AppendLine("            return result;");
                sb.AppendLine("        }");
                sb.AppendLine();
            }

            // CreateScopeCore
            sb.AppendLine("        protected override global::ZeroAlloc.Inject.Container.ZeroAllocInjectScope CreateScopeCore(IServiceScope fallbackScope)");
            sb.AppendLine("        {");
            sb.AppendLine("            return new Scope(this, fallbackScope);");
            sb.AppendLine("        }");
            sb.AppendLine();

            // Nested Scope class
            var scopeBase = "global::ZeroAlloc.Inject.Container.ZeroAllocInjectScope";
            if (hasKeyedServices)
            {
                scopeBase = scopeBase + ", IKeyedServiceProvider";
            }
            sb.AppendLine("        private sealed class Scope : " + scopeBase);
            sb.AppendLine("        {");

            // Scoped fields
            for (int i = 0; i < scopeds.Count; i++)
            {
                sb.AppendLine("            private " + scopeds[i].FullyQualifiedName + "? _scoped_" + i + ";");
                // Emit a cached-decorator field for each scoped service that has a decorated interface
                foreach (var st in GetServiceTypes(scopeds[i]))
                {
                    if (decoratorsByInterface.TryGetValue(st, out var decList))
                    {
                        // Use the outermost decorator type for the cached field
                        var outermost = decList[decList.Count - 1];
                        sb.AppendLine("            private " + outermost.DecoratorFqn + "? _scoped_" + i + "_d;");
                        break;
                    }
                }
            }
            for (int i = 0; i < keyedScopedServices.Count; i++)
            {
                sb.AppendLine("            private " + keyedScopedServices[i].FullyQualifiedName + "? _keyedScoped_" + i + ";");
            }
            if (scopeds.Count > 0 || keyedScopedServices.Count > 0)
            {
                sb.AppendLine();
            }

            // Scope constructor
            sb.AppendLine("            public Scope(" + className + " root, IServiceScope fallbackScope) : base(root, fallbackScope) { }");
            sb.AppendLine();

            // ResolveScopedKnown
            sb.AppendLine("            protected override object? ResolveScopedKnown(Type serviceType)");
            sb.AppendLine("            {");

            if (hasKeyedServices)
            {
                sb.AppendLine("                if (serviceType == typeof(IKeyedServiceProvider))");
                sb.AppendLine("                    return this;");
            }

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
                        var newExpr = BuildDecoratedNewExpression(svc, serviceType, decoratorsByInterface, true);
                        if (svc.ImplementsDisposable)
                        {
                            newExpr = "TrackDisposable(" + newExpr + ")";
                        }
                        sb.AppendLine("                if (serviceType == typeof(" + serviceType + "))");
                        sb.AppendLine("                    return " + newExpr + ";");
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
                    if (decoratorsByInterface.TryGetValue(serviceType, out var scopedDecoratorList))
                    {
                        // Decorated interface: cache the inner concrete, chain decorators, cache the outermost
                        var currentExpr = "(" + svc.FullyQualifiedName + ")" + fieldName;
                        foreach (var dec in scopedDecoratorList)
                        {
                            currentExpr = BuildNewExpressionWithDecorator(dec, svc.FullyQualifiedName,
                                currentExpr, serviceType);
                        }
                        sb.AppendLine("                    if (" + fieldName + " == null) " + fieldName + " = " + innerExpr + ";");
                        sb.AppendLine("                    if (" + fieldName + "_d == null) { " + fieldName + "_d = " + currentExpr + "; TrackDisposable(" + fieldName + "_d); }");
                        sb.AppendLine("                    return " + fieldName + "_d;");
                    }
                    else if (svc.ImplementsDisposable)
                    {
                        sb.AppendLine("                    if (" + fieldName + " == null) { " + fieldName + " = " + innerExpr + "; TrackDisposable(" + fieldName + "); }");
                        sb.AppendLine("                    return " + fieldName + ";");
                    }
                    else
                    {
                        sb.AppendLine("                    if (" + fieldName + " == null) " + fieldName + " = " + innerExpr + ";");
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
                sb.Append("                    return new " + serviceType + "[] { ");

                for (int j = 0; j < entries.Count; j++)
                {
                    if (j > 0) sb.Append(", ");
                    var entry = entries[j];

                    if (entry.Lifetime == "Transient")
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
                sb.AppendLine("                }");
            }

            sb.AppendLine("                return null;");
            sb.AppendLine("            }");

            // Keyed service methods in scope
            if (hasKeyedServices)
            {
                sb.AppendLine();
                sb.AppendLine("            public object? GetKeyedService(Type serviceType, object? serviceKey)");
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
                        sb.AppendLine("                        return ((" + className + ")Root).GetKeyedService(serviceType, serviceKey);");
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

                sb.AppendLine("                }");
                sb.AppendLine("                return null;");
                sb.AppendLine("            }");
                sb.AppendLine();
                sb.AppendLine("            public object GetRequiredKeyedService(Type serviceType, object? serviceKey)");
                sb.AppendLine("            {");
                sb.AppendLine("                var result = GetKeyedService(serviceType, serviceKey);");
                sb.AppendLine("                if (result == null) throw new InvalidOperationException($\"No keyed service of type '{serviceType}' with key '{serviceKey}' has been registered.\");");
                sb.AppendLine("                return result;");
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
            sb.AppendLine("    public static class ZeroAllocInjectServiceCollectionExtensions");
            sb.AppendLine("    {");
            sb.AppendLine("        public static IServiceProvider BuildZeroAllocInjectServiceProvider(this IServiceCollection services)");
            sb.AppendLine("        {");
            sb.AppendLine("            var fallback = services.BuildServiceProvider();");
            sb.AppendLine("            return new global::ZeroAlloc.Inject.Generated." + className + "(fallback);");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine();

            // ZeroAllocInjectServiceProviderFactory
            sb.AppendLine("    public sealed class ZeroAllocInjectServiceProviderFactory : IServiceProviderFactory<IServiceCollection>");
            sb.AppendLine("    {");
            sb.AppendLine("        public IServiceCollection CreateBuilder(IServiceCollection services) => services;");
            sb.AppendLine();
            sb.AppendLine("        public IServiceProvider CreateServiceProvider(IServiceCollection containerBuilder)");
            sb.AppendLine("        {");
            sb.AppendLine("            var fallback = containerBuilder.BuildServiceProvider();");
            sb.AppendLine("            return new global::ZeroAlloc.Inject.Generated." + className + "(fallback);");
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

            bool hasKeyedServices = keyedServices.Count > 0;

            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated />");
            sb.AppendLine("#nullable enable");
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Threading;");
            sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
            sb.AppendLine();
            sb.AppendLine("namespace ZeroAlloc.Inject.Generated");
            sb.AppendLine("{");
            var baseClass = "global::ZeroAlloc.Inject.Container.ZeroAllocInjectStandaloneProvider";
            if (hasKeyedServices)
            {
                baseClass = baseClass + ", IKeyedServiceProvider";
            }
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
                sb.AppendLine("        private " + singletons[i].FullyQualifiedName + "? _singleton_" + i + ";");
            }
            // Keyed singleton fields
            for (int i = 0; i < keyedSingletons.Count; i++)
            {
                sb.AppendLine("        private " + keyedSingletons[i].FullyQualifiedName + "? _keyedSingleton_" + i + ";");
            }
            // Closed generic singleton fields
            for (int i = 0; i < closedGenericFactories.Length; i++)
            {
                var cgf = closedGenericFactories[i];
                if (string.Equals(cgf.Lifetime, "Singleton", StringComparison.Ordinal))
                    sb.AppendLine("        private " + cgf.ImplementationFqn + "? _cg_s_" + i + ";");
            }
            if (singletons.Count > 0 || keyedSingletons.Count > 0 || closedGenericFactories.Length > 0)
            {
                sb.AppendLine();
            }

            // Constructor - parameterless
            sb.AppendLine("        public " + className + "() { }");
            sb.AppendLine();

            // ResolveKnown - root provider: transients + singletons (scoped returns null)
            sb.AppendLine("        protected override object? ResolveKnown(Type serviceType)");
            sb.AppendLine("        {");

            if (hasKeyedServices)
            {
                sb.AppendLine("            if (serviceType == typeof(IKeyedServiceProvider))");
                sb.AppendLine("                return this;");
            }

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
                        var newExpr = BuildDecoratedNewExpression(svc, serviceType, decoratorsByInterface, false);
                        sb.AppendLine("            if (serviceType == typeof(" + serviceType + "))");
                        sb.AppendLine("                return " + newExpr + ";");
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
                    sb.AppendLine("            {");
                    sb.AppendLine("                if (" + fieldName + " != null) return " + fieldName + ";");
                    sb.AppendLine("                var instance = " + newExpr + ";");
                    if (svc.ImplementsDisposable)
                    {
                        sb.AppendLine("                var existing = Interlocked.CompareExchange(ref " + fieldName + ", instance, null);");
                        sb.AppendLine("                if (existing != null) { (instance as System.IDisposable)?.Dispose(); return existing; }");
                        sb.AppendLine("                return " + fieldName + ";");
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
                sb.Append("                return new " + serviceType + "[] { ");

                for (int j = 0; j < rootEntries.Count; j++)
                {
                    if (j > 0) sb.Append(", ");
                    var entry = rootEntries[j];

                    if (entry.Lifetime == "Transient")
                    {
                        sb.Append(BuildNewExpression(entry.Svc));
                    }
                    else if (entry.Lifetime == "Singleton")
                    {
                        sb.Append("(" + serviceType + ")GetService(typeof(" + entry.Svc.FullyQualifiedName + "))!");
                    }
                }

                sb.AppendLine(" };");
                sb.AppendLine("            }");
            }

            // Explicit closed generic entries (Transient + Singleton; Scoped handled in ResolveScopedKnown)
            for (int i = 0; i < closedGenericFactories.Length; i++)
            {
                var cgf = closedGenericFactories[i];
                if (string.Equals(cgf.Lifetime, "Scoped", StringComparison.Ordinal)) continue;
                sb.AppendLine("            if (serviceType == typeof(" + cgf.InterfaceFqn + "))");
                if (string.Equals(cgf.Lifetime, "Singleton", StringComparison.Ordinal))
                {
                    sb.AppendLine("            {");
                    sb.AppendLine("                if (_cg_s_" + i + " != null) return _cg_s_" + i + ";");
                    sb.AppendLine("                var _cg_instance_" + i + " = " + BuildDecoratedClosedGenericExpr(cgf, decoratorsByInterface) + ";");
                    if (cgf.ImplementsDisposable)
                    {
                        sb.AppendLine("                var _cg_existing_" + i + " = Interlocked.CompareExchange(ref _cg_s_" + i + ", _cg_instance_" + i + ", null);");
                        sb.AppendLine("                if (_cg_existing_" + i + " != null) { (_cg_instance_" + i + " as global::System.IDisposable)?.Dispose(); return _cg_existing_" + i + "; }");
                        sb.AppendLine("                return _cg_s_" + i + "!;");
                    }
                    else
                    {
                        sb.AppendLine("                return Interlocked.CompareExchange(ref _cg_s_" + i + ", _cg_instance_" + i + ", null) ?? _cg_s_" + i + ";");
                    }
                    sb.AppendLine("            }");
                }
                else // Transient
                {
                    sb.AppendLine("                return " + BuildDecoratedClosedGenericExpr(cgf, decoratorsByInterface) + ";");
                }
            }

            sb.AppendLine("            return null;");
            sb.AppendLine("        }");
            sb.AppendLine();

            EmitIsKnownService(sb, serviceTypeGroups, hasKeyedServices);
            sb.AppendLine();

            EmitIsKnownKeyedService(sb, keyedServices);
            sb.AppendLine();

            // Keyed service methods
            if (hasKeyedServices)
            {
                sb.AppendLine("        public object? GetKeyedService(Type serviceType, object? serviceKey)");
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
                        if (svc.ImplementsDisposable)
                        {
                            sb.AppendLine("                    var existing = Interlocked.CompareExchange(ref " + fieldName + ", instance, null);");
                            sb.AppendLine("                    if (existing != null) { (instance as System.IDisposable)?.Dispose(); return existing; }");
                            sb.AppendLine("                    return " + fieldName + ";");
                        }
                        else
                        {
                            sb.AppendLine("                    return Interlocked.CompareExchange(ref " + fieldName + ", instance, null) ?? " + fieldName + ";");
                        }
                        sb.AppendLine("                }");
                    }
                }

                // Keyed transients - new instance each call
                foreach (var svc in keyedTransients)
                {
                    var serviceTypes = GetServiceTypes(svc);
                    var newExpr = BuildNewExpression(svc);
                    var escapedKey = svc.Key!.Replace("\\", "\\\\").Replace("\"", "\\\"");
                    foreach (var serviceType in serviceTypes)
                    {
                        sb.AppendLine("                if (serviceType == typeof(" + serviceType + ") && key == \"" + escapedKey + "\")");
                        sb.AppendLine("                    return " + newExpr + ";");
                    }
                }

                sb.AppendLine("            }");
                sb.AppendLine("            return null;");
                sb.AppendLine("        }");
                sb.AppendLine();
                sb.AppendLine("        public object GetRequiredKeyedService(Type serviceType, object? serviceKey)");
                sb.AppendLine("        {");
                sb.AppendLine("            var result = GetKeyedService(serviceType, serviceKey);");
                sb.AppendLine("            if (result == null) throw new InvalidOperationException($\"No keyed service of type '{serviceType}' with key '{serviceKey}' has been registered.\");");
                sb.AppendLine("            return result;");
                sb.AppendLine("        }");
                sb.AppendLine();
            }

            // CreateScopeCore - no parameter for standalone
            sb.AppendLine("        protected override global::ZeroAlloc.Inject.Container.ZeroAllocInjectStandaloneScope CreateScopeCore()");
            sb.AppendLine("        {");
            sb.AppendLine("            return new Scope(this);");
            sb.AppendLine("        }");
            sb.AppendLine();

            // Dispose/DisposeAsync overrides — only when there are disposable singletons
            var disposableSingletonIndices = new List<int>();
            for (int i = 0; i < singletons.Count; i++)
            {
                if (singletons[i].ImplementsDisposable)
                    disposableSingletonIndices.Add(i);
            }
            var disposableKeyedSingletonIndices = new List<int>();
            for (int i = 0; i < keyedSingletons.Count; i++)
            {
                if (keyedSingletons[i].ImplementsDisposable)
                    disposableKeyedSingletonIndices.Add(i);
            }

            if (disposableSingletonIndices.Count > 0 || disposableKeyedSingletonIndices.Count > 0
                || closedGenericFactories.Any(static cgf => cgf.ImplementsDisposable && cgf.Lifetime == "Singleton"))
            {
                // Dispose(bool) override
                sb.AppendLine("        protected override void Dispose(bool disposing)");
                sb.AppendLine("        {");
                sb.AppendLine("            base.Dispose(disposing);");
                sb.AppendLine("            if (disposing)");
                sb.AppendLine("            {");
                foreach (var idx in disposableSingletonIndices)
                {
                    var fieldName = "_singleton_" + idx;
                    sb.AppendLine("                var __s" + idx + " = Interlocked.Exchange(ref " + fieldName + ", null);");
                    sb.AppendLine("                (__s" + idx + " as System.IDisposable)?.Dispose();");
                }
                foreach (var idx in disposableKeyedSingletonIndices)
                {
                    var fieldName = "_keyedSingleton_" + idx;
                    sb.AppendLine("                var __ks" + idx + " = Interlocked.Exchange(ref " + fieldName + ", null);");
                    sb.AppendLine("                (__ks" + idx + " as System.IDisposable)?.Dispose();");
                }
                for (int cgIdx = 0; cgIdx < closedGenericFactories.Length; cgIdx++)
                {
                    var cgf = closedGenericFactories[cgIdx];
                    if (!cgf.ImplementsDisposable || cgf.Lifetime != "Singleton") continue;
                    sb.AppendLine("                var __cg_s_" + cgIdx + " = global::System.Threading.Interlocked.Exchange(ref _cg_s_" + cgIdx + ", null);");
                    sb.AppendLine("                (__cg_s_" + cgIdx + " as global::System.IDisposable)?.Dispose();");
                }
                sb.AppendLine("            }");
                sb.AppendLine("        }");
                sb.AppendLine();

                // DisposeAsync override
                sb.AppendLine("        public override async System.Threading.Tasks.ValueTask DisposeAsync()");
                sb.AppendLine("        {");
                foreach (var idx in disposableSingletonIndices)
                {
                    var fieldName = "_singleton_" + idx;
                    sb.AppendLine("            var __s" + idx + " = Interlocked.Exchange(ref " + fieldName + ", null);");
                    sb.AppendLine("            if (__s" + idx + " is System.IAsyncDisposable __ad" + idx + ") await __ad" + idx + ".DisposeAsync().ConfigureAwait(false);");
                    sb.AppendLine("            else (__s" + idx + " as System.IDisposable)?.Dispose();");
                }
                foreach (var idx in disposableKeyedSingletonIndices)
                {
                    var fieldName = "_keyedSingleton_" + idx;
                    sb.AppendLine("            var __ks" + idx + " = Interlocked.Exchange(ref " + fieldName + ", null);");
                    sb.AppendLine("            if (__ks" + idx + " is System.IAsyncDisposable __kad" + idx + ") await __kad" + idx + ".DisposeAsync().ConfigureAwait(false);");
                    sb.AppendLine("            else (__ks" + idx + " as System.IDisposable)?.Dispose();");
                }
                for (int cgIdx = 0; cgIdx < closedGenericFactories.Length; cgIdx++)
                {
                    var cgf = closedGenericFactories[cgIdx];
                    if (!cgf.ImplementsDisposable || cgf.Lifetime != "Singleton") continue;
                    sb.AppendLine("            var __cg_sa_" + cgIdx + " = global::System.Threading.Interlocked.Exchange(ref _cg_s_" + cgIdx + ", null);");
                    sb.AppendLine("            if (__cg_sa_" + cgIdx + " is global::System.IAsyncDisposable __cg_sad_" + cgIdx + ")");
                    sb.AppendLine("                await __cg_sad_" + cgIdx + ".DisposeAsync().ConfigureAwait(false);");
                    sb.AppendLine("            else (__cg_sa_" + cgIdx + " as global::System.IDisposable)?.Dispose();");
                }
                sb.AppendLine("            await base.DisposeAsync().ConfigureAwait(false);");
                sb.AppendLine("        }");
                sb.AppendLine();
            }

            // Nested Scope class
            var scopeBase = "global::ZeroAlloc.Inject.Container.ZeroAllocInjectStandaloneScope";
            if (hasKeyedServices)
            {
                scopeBase = scopeBase + ", IKeyedServiceProvider";
            }
            sb.AppendLine("        private sealed class Scope : " + scopeBase);
            sb.AppendLine("        {");

            // Scoped fields
            for (int i = 0; i < scopeds.Count; i++)
            {
                sb.AppendLine("            private " + scopeds[i].FullyQualifiedName + "? _scoped_" + i + ";");
                // Emit a cached-decorator field for each scoped service that has a decorated interface
                foreach (var st in GetServiceTypes(scopeds[i]))
                {
                    if (decoratorsByInterface.TryGetValue(st, out var decList))
                    {
                        // Use the outermost decorator type for the cached field
                        var outermost = decList[decList.Count - 1];
                        sb.AppendLine("            private " + outermost.DecoratorFqn + "? _scoped_" + i + "_d;");
                        break;
                    }
                }
            }
            for (int i = 0; i < keyedScopedServices.Count; i++)
            {
                sb.AppendLine("            private " + keyedScopedServices[i].FullyQualifiedName + "? _keyedScoped_" + i + ";");
            }
            // Closed generic scoped fields
            for (int i = 0; i < closedGenericFactories.Length; i++)
            {
                var cgf = closedGenericFactories[i];
                if (string.Equals(cgf.Lifetime, "Scoped", StringComparison.Ordinal))
                    sb.AppendLine("            private " + cgf.ImplementationFqn + "? _cg_sc_" + i + ";");
            }
            bool hasScopedCgFields = false;
            for (int i = 0; i < closedGenericFactories.Length; i++)
            {
                if (string.Equals(closedGenericFactories[i].Lifetime, "Scoped", StringComparison.Ordinal))
                { hasScopedCgFields = true; break; }
            }
            if (scopeds.Count > 0 || keyedScopedServices.Count > 0 || hasScopedCgFields)
            {
                sb.AppendLine();
            }

            // Scope constructor - only root, no fallbackScope
            sb.AppendLine("            public Scope(" + className + " root) : base(root) { }");
            sb.AppendLine();

            // ResolveScopedKnown
            sb.AppendLine("            protected override object? ResolveScopedKnown(Type serviceType)");
            sb.AppendLine("            {");

            if (hasKeyedServices)
            {
                sb.AppendLine("                if (serviceType == typeof(IKeyedServiceProvider))");
                sb.AppendLine("                    return this;");
            }

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
                        var newExpr = BuildDecoratedNewExpression(svc, serviceType, decoratorsByInterface, true);
                        if (svc.ImplementsDisposable)
                        {
                            newExpr = "TrackDisposable(" + newExpr + ")";
                        }
                        sb.AppendLine("                if (serviceType == typeof(" + serviceType + "))");
                        sb.AppendLine("                    return " + newExpr + ";");
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
                    if (decoratorsByInterface.TryGetValue(serviceType, out var scopedDecoratorList))
                    {
                        // Decorated interface: cache the inner concrete, chain decorators, cache the outermost
                        var currentExpr = "(" + svc.FullyQualifiedName + ")" + fieldName;
                        foreach (var dec in scopedDecoratorList)
                        {
                            currentExpr = BuildNewExpressionWithDecorator(dec, svc.FullyQualifiedName,
                                currentExpr, serviceType);
                        }
                        sb.AppendLine("                    if (" + fieldName + " == null) " + fieldName + " = " + innerExpr + ";");
                        sb.AppendLine("                    if (" + fieldName + "_d == null) { " + fieldName + "_d = " + currentExpr + "; TrackDisposable(" + fieldName + "_d); }");
                        sb.AppendLine("                    return " + fieldName + "_d;");
                    }
                    else if (svc.ImplementsDisposable)
                    {
                        sb.AppendLine("                    if (" + fieldName + " == null) { " + fieldName + " = " + innerExpr + "; TrackDisposable(" + fieldName + "); }");
                        sb.AppendLine("                    return " + fieldName + ";");
                    }
                    else
                    {
                        sb.AppendLine("                    if (" + fieldName + " == null) " + fieldName + " = " + innerExpr + ";");
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
                sb.Append("                    return new " + serviceType + "[] { ");

                for (int j = 0; j < entries.Count; j++)
                {
                    if (j > 0) sb.Append(", ");
                    var entry = entries[j];

                    if (entry.Lifetime == "Transient")
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
                sb.AppendLine("                }");
            }

            // Explicit closed generic entries in scope (all lifetimes)
            for (int i = 0; i < closedGenericFactories.Length; i++)
            {
                var cgf = closedGenericFactories[i];
                sb.AppendLine("                if (serviceType == typeof(" + cgf.InterfaceFqn + "))");
                if (string.Equals(cgf.Lifetime, "Singleton", StringComparison.Ordinal))
                {
                    sb.AppendLine("                    return Root.GetService(serviceType);");
                }
                else if (string.Equals(cgf.Lifetime, "Scoped", StringComparison.Ordinal))
                {
                    sb.AppendLine("                {");
                    if (cgf.ImplementsDisposable)
                    {
                        sb.AppendLine("                    if (_cg_sc_" + i + " == null) { _cg_sc_" + i + " = " + BuildDecoratedClosedGenericExpr(cgf, decoratorsByInterface) + "; TrackDisposable(_cg_sc_" + i + "); }");
                    }
                    else
                    {
                        sb.AppendLine("                    if (_cg_sc_" + i + " == null) _cg_sc_" + i + " = " + BuildDecoratedClosedGenericExpr(cgf, decoratorsByInterface) + ";");
                    }
                    sb.AppendLine("                    return _cg_sc_" + i + ";");
                    sb.AppendLine("                }");
                }
                else // Transient — fresh instance each call
                {
                    sb.AppendLine("                    return " + BuildDecoratedClosedGenericExpr(cgf, decoratorsByInterface) + ";");
                }
            }

            sb.AppendLine("                return null;");
            sb.AppendLine("            }");


            // Keyed service methods in scope
            if (hasKeyedServices)
            {
                sb.AppendLine();
                sb.AppendLine("            public object? GetKeyedService(Type serviceType, object? serviceKey)");
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
                        sb.AppendLine("                        return ((" + className + ")Root).GetKeyedService(serviceType, serviceKey);");
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

                sb.AppendLine("                }");
                sb.AppendLine("                return null;");
                sb.AppendLine("            }");
                sb.AppendLine();
                sb.AppendLine("            public object GetRequiredKeyedService(Type serviceType, object? serviceKey)");
                sb.AppendLine("            {");
                sb.AppendLine("                var result = GetKeyedService(serviceType, serviceKey);");
                sb.AppendLine("                if (result == null) throw new InvalidOperationException($\"No keyed service of type '{serviceType}' with key '{serviceKey}' has been registered.\");");
                sb.AppendLine("                return result;");
                sb.AppendLine("            }");
            }

            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            return sb.ToString();
        }

        private static void EmitIsKnownService(
            StringBuilder sb,
            Dictionary<string, List<ServiceTypeGroupEntry>> serviceTypeGroups,
            bool hasKeyedServices)
        {
            sb.AppendLine("        protected override bool IsKnownService(global::System.Type serviceType)");
            sb.AppendLine("        {");

            if (hasKeyedServices)
            {
                sb.AppendLine("            if (serviceType == typeof(global::Microsoft.Extensions.DependencyInjection.IKeyedServiceProvider)) return true;");
                sb.AppendLine("            if (serviceType == typeof(global::Microsoft.Extensions.DependencyInjection.IServiceProviderIsKeyedService)) return true;");
            }

            // Closed types (includes explicit closed generic entries from FindClosedGenericUsages)
            foreach (var kvp in serviceTypeGroups)
            {
                sb.AppendLine("            if (serviceType == typeof(" + kvp.Key + ")) return true;");
            }

            sb.AppendLine("            return false;");
            sb.AppendLine("        }");
        }

        private static void EmitIsKnownKeyedService(
            StringBuilder sb,
            List<ServiceRegistrationInfo> keyedServices)
        {
            sb.AppendLine("        protected override bool IsKnownKeyedService(global::System.Type serviceType, object? serviceKey)");
            sb.AppendLine("        {");

            if (keyedServices.Count > 0)
            {
                sb.AppendLine("            if (serviceKey is string key)");
                sb.AppendLine("            {");

                foreach (var svc in keyedServices)
                {
                    var serviceTypes = GetServiceTypes(svc);
                    var escapedKey = svc.Key!.Replace("\\", "\\\\").Replace("\"", "\\\"");
                    foreach (var serviceType in serviceTypes)
                    {
                        sb.AppendLine("                if (serviceType == typeof(" + serviceType + ") && key == \"" + escapedKey + "\") return true;");
                    }
                }

                sb.AppendLine("            }");
            }

            sb.AppendLine("            return false;");
            sb.AppendLine("        }");
        }

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
            SourceProductionContext spc,
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
                    DfsCycleDetect(node, adjacency, color, parent, spc, reportedCycles);
                }
            }
        }

        private static void DfsCycleDetect(
            string node,
            Dictionary<string, List<string>> adjacency,
            Dictionary<string, int> color,
            Dictionary<string, string?> parent,
            SourceProductionContext spc,
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
                        DfsCycleDetect(dep, adjacency, color, parent, spc, reportedCycles);
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
                            spc.ReportDiagnostic(Diagnostic.Create(
                                DiagnosticDescriptors.CircularDependency,
                                Location.None,
                                cyclePath));
                        }
                    }
                }
            }

            color[node] = 2; // black
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

        /// <summary>Builds a "new ClosedImpl(args...)" expression for an explicit closed-generic entry.</summary>
        private static string BuildClosedGenericNewExpr(ClosedGenericFactoryInfo cgf)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("new ").Append(cgf.ImplementationFqn).Append("(");
            for (int i = 0; i < cgf.Parameters.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                var p = cgf.Parameters[i];
                if (p.IsOptional)
                {
                    sb.Append("(").Append(p.FullyQualifiedTypeName).Append("?)GetService(typeof(")
                      .Append(p.FullyQualifiedTypeName).Append("))");
                }
                else
                {
                    sb.Append("(").Append(p.FullyQualifiedTypeName).Append(")GetService(typeof(")
                      .Append(p.FullyQualifiedTypeName).Append("))!");
                }
            }
            sb.Append(")");
            return sb.ToString();
        }

        /// <summary>
        /// Builds the instantiation expression for a closed generic factory, wrapping with decorator chain
        /// if an open-generic decorator is registered for the interface's unbound form.
        /// </summary>
        private static string BuildDecoratedClosedGenericExpr(
            ClosedGenericFactoryInfo cgf,
            System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<DecoratorRegistrationInfo>> decoratorsByInterface)
        {
            var innerExpr = BuildClosedGenericNewExpr(cgf);

            // Derive unbound FQN (e.g. "global::IRepo<>" from "global::IRepo<string>")
            var unboundFqn = GetUnboundFqnFromClosed(cgf.InterfaceFqn);
            if (unboundFqn == null) return innerExpr;

            if (!decoratorsByInterface.TryGetValue(unboundFqn, out var decorators) || decorators.Count == 0)
                return innerExpr;

            var typeArgs = ExtractTypeArgsFromClosedFqn(cgf.InterfaceFqn);
            if (typeArgs.Length == 0) return innerExpr;

            // Chain decorators innermost-first (list is already sorted by Order ascending)
            var currentExpr = innerExpr;
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
                    if (closedParamType == closedInterfaceFqn || param.FullyQualifiedTypeName == unboundFqn)
                    {
                        sb.Append("(").Append(closedInterfaceFqn).Append(")(").Append(currentExpr).Append(")");
                    }
                    else if (param.IsOptional)
                    {
                        sb.Append("(").Append(closedParamType).Append("?)GetService(typeof(")
                          .Append(closedParamType).Append("))");
                    }
                    else
                    {
                        sb.Append("(").Append(closedParamType).Append(")GetService(typeof(")
                          .Append(closedParamType).Append("))!");
                    }
                }
                sb.Append(")");
                currentExpr = sb.ToString();
            }
            return currentExpr;
        }

        /// <summary>Returns the unbound generic FQN from a closed generic FQN, e.g. "global::IFoo&lt;global::Bar&gt;" → "global::IFoo&lt;&gt;".</summary>
        private static string? GetUnboundFqnFromClosed(string closedFqn)
        {
            var idx = closedFqn.IndexOf('<');
            if (idx < 0) return null;
            var prefix = closedFqn.Substring(0, idx);
            var typeArgs = ExtractTypeArgsFromClosedFqn(closedFqn);
            if (typeArgs.Length == 0) return null;
            var commas = typeArgs.Length > 1 ? new string(',', typeArgs.Length - 1) : "";
            return prefix + "<" + commas + ">";
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
            bool isOpenGeneric,
            List<ConstructorParameterInfo> constructorParameters)
        {
            if (isOpenGeneric)
            {
                var addOrTryAdd = useAdd ? "Add" : "TryAdd";
                sb.AppendLine(string.Format(
                    "            services.{0}(ServiceDescriptor.{1}(typeof({2}), typeof({3})));",
                    addOrTryAdd, lifetime, implType, implType));
                return;
            }

            if (key != null)
            {
                var method = useAdd ? "AddKeyed" + lifetime : "TryAddKeyed" + lifetime;
                var factory = BuildKeyedFactoryLambda(implType, constructorParameters);
                var escapedKey = key.Replace("\\", "\\\\").Replace("\"", "\\\"");
                sb.AppendLine(string.Format(
                    "            services.{0}<{1}>(\"{2}\", {3});",
                    method, implType, escapedKey, factory));
            }
            else
            {
                var method = useAdd ? "Add" + lifetime : "TryAdd" + lifetime;
                var factory = BuildFactoryLambda(implType, constructorParameters);
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
                order: 0, whenRegisteredFqn: null, isDecoratorOf: false);
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
                order: order, whenRegisteredFqn: whenRegisteredFqn, isDecoratorOf: true);
        }

        private static ImmutableArray<ClosedGenericFactoryInfo> FindClosedGenericUsages(
            (((ImmutableArray<ServiceRegistrationInfo?> transients,
               ImmutableArray<ServiceRegistrationInfo?> scopeds),
              ImmutableArray<ServiceRegistrationInfo?> singletons),
             Compilation compilation) data,
            CancellationToken ct)
        {
            var transients  = data.Item1.Item1.Item1;
            var scopeds     = data.Item1.Item1.Item2;
            var singletons  = data.Item1.Item2;
            var compilation = data.Item2;

            // Build lookup: unbound interface FQN (global::IFoo<,> form) → open generic ServiceRegistrationInfo
            var openGenericMap = new Dictionary<string, ServiceRegistrationInfo>(StringComparer.Ordinal);
            foreach (var svc in transients.Concat(scopeds).Concat(singletons))
            {
                if (svc == null || !svc.IsOpenGeneric || svc.ImplementationMetadataName == null) continue;
                var ifaces = svc.AsType != null ? new List<string> { svc.AsType } : svc.Interfaces;
                foreach (var iface in ifaces)
                    if (!openGenericMap.ContainsKey(iface))
                        openGenericMap[iface] = svc;
            }

            if (openGenericMap.Count == 0) return ImmutableArray<ClosedGenericFactoryInfo>.Empty;

            // Seed work queue from all constructor parameters of all registered services
            var workQueue = new Queue<ConstructorParameterInfo>();
            var processed = new HashSet<string>(StringComparer.Ordinal); // keyed by closed interface FQN
            var results = new List<ClosedGenericFactoryInfo>();

            foreach (var svc in transients.Concat(scopeds).Concat(singletons))
            {
                if (svc == null) continue;
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
                if (!openGenericMap.TryGetValue(param.UnboundGenericInterfaceFqn, out var og)) continue;

                // Resolve impl symbol and close it
                var implSymbol = compilation.GetTypeByMetadataName(og.ImplementationMetadataName!);
                if (implSymbol == null) continue;

                var typeArgSymbols = new ITypeSymbol[param.TypeArgumentMetadataNames.Length];
                bool allResolved = true;
                for (int i = 0; i < param.TypeArgumentMetadataNames.Length; i++)
                {
                    var sym = compilation.GetTypeByMetadataName(param.TypeArgumentMetadataNames[i]);
                    if (sym == null) { allResolved = false; break; }
                    typeArgSymbols[i] = sym;
                }
                if (!allResolved) continue;

                var closedImpl = implSymbol.Construct(typeArgSymbols);
                var closedImplFqn = closedImpl.ToDisplayString(FullyQualifiedFormat);

                bool implementsDisposable = closedImpl.AllInterfaces.Any(static i =>
                    i.SpecialType == SpecialType.System_IDisposable);

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
                    ImmutableArray<string> ctorTypeArgMeta = ImmutableArray<string>.Empty;

                    if (ctorParam.Type is INamedTypeSymbol namedCtorParam
                        && namedCtorParam.IsGenericType && !namedCtorParam.IsUnboundGenericType)
                    {
                        var rawUnbound = namedCtorParam.ConstructedFrom.ToDisplayString(FullyQualifiedFormat);
                        ctorUnboundFqn = ToUnboundGenericString(rawUnbound, namedCtorParam.TypeArguments.Length);
                        var metaBuilder = ImmutableArray.CreateBuilder<string>(namedCtorParam.TypeArguments.Length);
                        foreach (var ta in namedCtorParam.TypeArguments)
                        {
                            var taNamespace = ta.ContainingNamespace is { IsGlobalNamespace: false } taNs
                                ? taNs.ToDisplayString() : null;
                            metaBuilder.Add(taNamespace != null
                                ? taNamespace + "." + ta.MetadataName
                                : ta.MetadataName);
                        }
                        ctorTypeArgMeta = metaBuilder.ToImmutable();
                    }

                    var ctorParamInfo = new ConstructorParameterInfo(
                        ctorParamFqn, ctorParam.Name, false, ctorUnboundFqn, ctorTypeArgMeta);
                    // Add to work queue for fixed-point iteration if it is a closed generic
                    if (ctorUnboundFqn != null)
                        workQueue.Enqueue(ctorParamInfo);
                    ctorParams.Add(ctorParamInfo);
                }

                results.Add(new ClosedGenericFactoryInfo(
                    closedFqn,
                    closedImplFqn,
                    og.Lifetime,
                    ctorParams.ToImmutable(),
                    implementsDisposable));
            }

            return results.ToImmutableArray();
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
