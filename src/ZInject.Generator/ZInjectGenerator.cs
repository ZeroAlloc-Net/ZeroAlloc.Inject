#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace ZeroInject.Generator
{
    [Generator]
    public sealed class ZeroInjectGenerator : IIncrementalGenerator
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
                "ZeroInject.TransientAttribute",
                predicate: static (node, _) => true,
                transform: static (ctx, ct) => GetServiceInfo(ctx, "Transient", ct))
                .Where(static x => x != null)
                .Collect();

            var scopeds = context.SyntaxProvider.ForAttributeWithMetadataName(
                "ZeroInject.ScopedAttribute",
                predicate: static (node, _) => true,
                transform: static (ctx, ct) => GetServiceInfo(ctx, "Scoped", ct))
                .Where(static x => x != null)
                .Collect();

            var singletons = context.SyntaxProvider.ForAttributeWithMetadataName(
                "ZeroInject.SingletonAttribute",
                predicate: static (node, _) => true,
                transform: static (ctx, ct) => GetServiceInfo(ctx, "Singleton", ct))
                .Where(static x => x != null)
                .Collect();

            var assemblyAttr = context.SyntaxProvider.ForAttributeWithMetadataName(
                "ZeroInject.ZeroInjectAttribute",
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
                        if (asm.Name == "ZeroInject.Container")
                        {
                            return true;
                        }
                    }
                    return false;
                });

            var decorators = context.SyntaxProvider.ForAttributeWithMetadataName(
                "ZeroInject.DecoratorAttribute",
                predicate: static (node, _) => true,
                transform: static (ctx, ct) => GetDecoratorInfo(ctx, ct))
                .Where(static x => x != null)
                .Collect();

            var combined = transients
                .Combine(scopeds)
                .Combine(singletons)
                .Combine(assemblyAttr)
                .Combine(assemblyName)
                .Combine(hasContainer)
                .Combine(decorators);

            context.RegisterSourceOutput(combined, static (spc, data) =>
            {
                var transientInfos = data.Left.Left.Left.Left.Left.Left;
                var scopedInfos    = data.Left.Left.Left.Left.Left.Right;
                var singletonInfos = data.Left.Left.Left.Left.Right;
                var methodNameOverrides = data.Left.Left.Left.Right;
                var asmName        = data.Left.Left.Right;
                var containerReferenced = data.Left.Right;
                var decoratorInfos = data.Right;

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
                        spc.ReportDiagnostic(Diagnostic.Create(
                            DiagnosticDescriptors.DecoratorNoMatchingInterface,
                            Location.None, dec.TypeName));
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

                DetectCircularDependencies(spc, allServices, decoratorsByInterface);

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
                spc.AddSource("ZeroInject.ServiceCollectionExtensions.g.cs", source);

                if (containerReferenced)
                {
                    var providerSource = GenerateServiceProviderClass(allServices, asmName, decoratorsByInterface);
                    spc.AddSource("ZeroInject.ServiceProvider.g.cs", providerSource);

                    var standaloneCode = GenerateStandaloneServiceProviderClass(allServices, asmName, decoratorsByInterface);
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
                    bool isOptional = param.HasExplicitDefaultValue;

                    constructorParameters.Add(new ConstructorParameterInfo(
                        paramTypeFqn,
                        param.Name,
                        isOptional));

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
                implementsDisposable);
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

            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated />");
            sb.AppendLine("#nullable enable");
            sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
            sb.AppendLine("using Microsoft.Extensions.DependencyInjection.Extensions;");
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
                    // Emit factory wrapping with chained decorators
                    var decoratorFactory = BuildDecoratorFactoryLambdaChained(decorators, fqn);
                    sb.AppendLine(string.Format(
                        "            services.Add{0}<{1}>({2});",
                        lifetime, iface, decoratorFactory));
                }
                else
                {
                    EmitSingleRegistration(sb, lifetime, iface, fqn, svc.Key, useAdd, svc.IsOpenGeneric, svc.ConstructorParameters);
                }
            }

            // Always register concrete type (inner needs to be resolvable by itself)
            EmitConcreteRegistration(sb, lifetime, fqn, svc.Key, useAdd, svc.IsOpenGeneric, svc.ConstructorParameters);
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
            sb.AppendLine("namespace ZeroInject.Generated");
            sb.AppendLine("{");
            var baseClass = "global::ZeroInject.Container.ZeroInjectServiceProviderBase";
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

            EmitIsKnownService(sb, serviceTypeGroups, new List<ServiceRegistrationInfo>(), hasKeyedServices);
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
            sb.AppendLine("        protected override global::ZeroInject.Container.ZeroInjectScope CreateScopeCore(IServiceScope fallbackScope)");
            sb.AppendLine("        {");
            sb.AppendLine("            return new Scope(this, fallbackScope);");
            sb.AppendLine("        }");
            sb.AppendLine();

            // Nested Scope class
            var scopeBase = "global::ZeroInject.Container.ZeroInjectScope";
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

            // BuildZeroInjectServiceProvider extension method
            sb.AppendLine("    public static class ZeroInjectServiceCollectionExtensions");
            sb.AppendLine("    {");
            sb.AppendLine("        public static IServiceProvider BuildZeroInjectServiceProvider(this IServiceCollection services)");
            sb.AppendLine("        {");
            sb.AppendLine("            var fallback = services.BuildServiceProvider();");
            sb.AppendLine("            return new global::ZeroInject.Generated." + className + "(fallback);");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine();

            // ZeroInjectServiceProviderFactory
            sb.AppendLine("    public sealed class ZeroInjectServiceProviderFactory : IServiceProviderFactory<IServiceCollection>");
            sb.AppendLine("    {");
            sb.AppendLine("        public IServiceCollection CreateBuilder(IServiceCollection services) => services;");
            sb.AppendLine();
            sb.AppendLine("        public IServiceProvider CreateServiceProvider(IServiceCollection containerBuilder)");
            sb.AppendLine("        {");
            sb.AppendLine("            var fallback = containerBuilder.BuildServiceProvider();");
            sb.AppendLine("            return new global::ZeroInject.Generated." + className + "(fallback);");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            return sb.ToString();
        }

        private static string GenerateStandaloneServiceProviderClass(
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
            sb.AppendLine("namespace ZeroInject.Generated");
            sb.AppendLine("{");
            var baseClass = "global::ZeroInject.Container.ZeroInjectStandaloneProvider";
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

            // Constructor - parameterless
            sb.AppendLine("        public " + className + "() { }");
            sb.AppendLine();

            // Open generic: static MethodInfo refs + delegate caches + singleton caches + factory methods
            if (openGenerics.Count > 0)
            {
                sb.AppendLine("        // Open generic factory infra (code-generated delegate caches)");
                const string delegateType = "global::System.Func<global::System.IServiceProvider, object>";
                const string dictType = "global::System.Collections.Concurrent.ConcurrentDictionary<global::System.Type, " + delegateType + ">";
                const string singletonDictType = "global::System.Collections.Concurrent.ConcurrentDictionary<global::System.Type, object>";
                for (int ogIdx = 0; ogIdx < openGenerics.Count; ogIdx++)
                {
                    if (openGenerics[ogIdx].Key != null) continue;
                    sb.AppendLine("        private static readonly global::System.Reflection.MethodInfo _og_mi_" + ogIdx + " =");
                    sb.AppendLine("            typeof(" + className + ").GetMethod(\"OG_Factory_" + ogIdx + "\", global::System.Reflection.BindingFlags.NonPublic | global::System.Reflection.BindingFlags.Static)!;");
                    sb.AppendLine("        private static readonly " + dictType + " _og_dc_" + ogIdx + " = new();");
                    if (string.Equals(openGenerics[ogIdx].Lifetime, "Singleton", StringComparison.Ordinal))
                        sb.AppendLine("        private static readonly " + singletonDictType + " _og_sc_" + ogIdx + " = new();");
                }
                sb.AppendLine();
                for (int ogIdx = 0; ogIdx < openGenerics.Count; ogIdx++)
                {
                    var svc = openGenerics[ogIdx];
                    if (svc.Key != null) continue;
                    int arity = int.Parse(svc.OpenGenericArity!);
                    var typeParams = BuildOpenGenericTypeParams(arity);
                    var ifaces = svc.AsType != null ? new List<string> { svc.AsType } : svc.Interfaces;
                    List<DecoratorRegistrationInfo>? decoratorList = null;
                    foreach (var iface in ifaces)
                    {
                        if (decoratorsByInterface.TryGetValue(iface, out var decList))
                        {
                            // Filter to only open generic decorators
                            var ogDecs = new List<DecoratorRegistrationInfo>();
                            foreach (var d in decList)
                            {
                                if (d.IsOpenGeneric)
                                    ogDecs.Add(d);
                            }
                            if (ogDecs.Count > 0)
                            {
                                decoratorList = ogDecs;
                                break;
                            }
                        }
                    }
                    EmitOpenGenericFactoryMethod(sb, "OG_Factory_" + ogIdx, typeParams, svc, decoratorList, arity);
                }
                sb.AppendLine();
            }

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

            if (openGenerics.Count > 0)
                EmitOpenGenericRootResolve(sb, openGenerics, "            ", className);
            sb.AppendLine("            return null;");
            sb.AppendLine("        }");
            sb.AppendLine();

            EmitIsKnownService(sb, serviceTypeGroups, openGenerics, hasKeyedServices);
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
            sb.AppendLine("        protected override global::ZeroInject.Container.ZeroInjectStandaloneScope CreateScopeCore()");
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

            if (disposableSingletonIndices.Count > 0 || disposableKeyedSingletonIndices.Count > 0)
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
                sb.AppendLine("            await base.DisposeAsync().ConfigureAwait(false);");
                sb.AppendLine("        }");
                sb.AppendLine();
            }

            // Nested Scope class
            var scopeBase = "global::ZeroInject.Container.ZeroInjectStandaloneScope";
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

            if (openGenerics.Count > 0)
                EmitOpenGenericScopeResolve(sb, openGenerics, "                ", className);
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
            List<ServiceRegistrationInfo> openGenerics,
            bool hasKeyedServices)
        {
            sb.AppendLine("        protected override bool IsKnownService(global::System.Type serviceType)");
            sb.AppendLine("        {");

            if (hasKeyedServices)
            {
                sb.AppendLine("            if (serviceType == typeof(global::Microsoft.Extensions.DependencyInjection.IKeyedServiceProvider)) return true;");
            }

            // Closed types
            foreach (var kvp in serviceTypeGroups)
            {
                sb.AppendLine("            if (serviceType == typeof(" + kvp.Key + ")) return true;");
            }

            // Open generics
            if (openGenerics.Count > 0)
            {
                sb.AppendLine("            if (serviceType.IsGenericType)");
                sb.AppendLine("            {");
                sb.AppendLine("                var _genDef = serviceType.GetGenericTypeDefinition();");
                foreach (var svc in openGenerics)
                {
                    foreach (var st in GetServiceTypes(svc))
                    {
                        sb.AppendLine("                if (_genDef == typeof(" + st + ")) return true;");
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

        // ---- Open generic factory code-gen helpers ----

        /// <summary>Returns the generic type-parameter list for a factory method, e.g. "&lt;T&gt;" (arity 1) or "&lt;T1, T2&gt;" (arity 2).</summary>
        private static string BuildOpenGenericTypeParams(int arity)
        {
            if (arity == 1) return "<T>";
            var names = new System.Text.StringBuilder("<");
            for (int i = 1; i <= arity; i++)
            {
                if (i > 1) names.Append(", ");
                names.Append('T').Append(i);
            }
            names.Append('>');
            return names.ToString();
        }

        /// <summary>Closes an unbound generic FQN with method type params, e.g. "global::IFoo&lt;&gt;" → "global::IFoo&lt;T&gt;".</summary>
        private static string CloseGenericFqn(string fqn, int arity)
        {
            var idx = fqn.IndexOf('<');
            if (idx < 0) return fqn;
            var prefix = fqn.Substring(0, idx);
            if (arity == 1) return prefix + "<T>";
            var args = new System.Text.StringBuilder();
            for (int i = 1; i <= arity; i++)
            {
                if (i > 1) args.Append(", ");
                args.Append('T').Append(i);
            }
            return prefix + "<" + args.ToString() + ">";
        }

        /// <summary>
        /// Emits the OG_Factory method body. Uses expression body for simple cases,
        /// block body when a decorator must be created from an intermediate variable.
        /// </summary>
        private static void EmitOpenGenericFactoryMethod(
            StringBuilder sb,
            string methodName,
            string typeParams,
            ServiceRegistrationInfo svc,
            List<DecoratorRegistrationInfo>? decorators,
            int arity)
        {
            var closedImpl = CloseGenericFqn(svc.FullyQualifiedName, arity);
            var innerExpr = BuildOpenGenericNewExpr(closedImpl, svc.ConstructorParameters, null, null, arity);

            if (decorators == null || decorators.Count == 0)
            {
                sb.AppendLine("        private static object " + methodName + typeParams + "(global::System.IServiceProvider sp)");
                sb.AppendLine("            => " + innerExpr + ";");
                return;
            }

            // Decorator case: emit block body chaining all decorators
            sb.AppendLine("        private static object " + methodName + typeParams + "(global::System.IServiceProvider sp)");
            sb.AppendLine("        {");
            sb.AppendLine("            var _layer0 = " + innerExpr + ";");

            for (int i = 0; i < decorators.Count; i++)
            {
                var dec = decorators[i];
                var closedDecorator = CloseGenericFqn(dec.DecoratorFqn, arity);
                var decoratedIface = dec.DecoratedInterfaceFqn;
                var prevVar = "_layer" + i;
                var decoratorExpr = BuildOpenGenericNewExpr(closedDecorator, dec.ConstructorParameters, decoratedIface, prevVar, arity);

                if (i < decorators.Count - 1)
                {
                    var nextVar = "_layer" + (i + 1);
                    sb.AppendLine("            var " + nextVar + " = " + decoratorExpr + ";");
                }
                else
                {
                    sb.AppendLine("            return " + decoratorExpr + ";");
                }
            }

            sb.AppendLine("        }");
        }

        /// <summary>Builds a "new ClosedType(args...)" expression for a generic factory method body.</summary>
        private static string BuildOpenGenericNewExpr(
            string closedFqn,
            List<ConstructorParameterInfo> ctorParams,
            string? innerParamUnboundType,
            string? innerExprToInject,
            int arity)
        {
            if (ctorParams.Count == 0)
                return "new " + closedFqn + "()";

            var sb = new System.Text.StringBuilder("new ").Append(closedFqn).Append('(');
            for (int i = 0; i < ctorParams.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                var param = ctorParams[i];
                bool isInner = innerParamUnboundType != null &&
                               string.Equals(param.FullyQualifiedTypeName, innerParamUnboundType, StringComparison.Ordinal);
                if (isInner && innerExprToInject != null)
                {
                    var closedParamType = CloseGenericFqn(param.FullyQualifiedTypeName, arity);
                    sb.Append('(').Append(closedParamType).Append(")(").Append(innerExprToInject).Append(')');
                }
                else
                {
                    bool isUnbound = param.FullyQualifiedTypeName.Contains('<');
                    if (isUnbound)
                    {
                        var closedParamType = CloseGenericFqn(param.FullyQualifiedTypeName, arity);
                        // Resolve via MakeGenericType at runtime
                        var typeofArgs = BuildTypeofArgs(arity);
                        var serviceTypeExpr = "typeof(" + param.FullyQualifiedTypeName + ").MakeGenericType(" + typeofArgs + ")";
                        if (param.IsOptional)
                            sb.Append('(').Append(closedParamType).Append("?)sp.GetService(").Append(serviceTypeExpr).Append(')');
                        else
                            sb.Append('(').Append(closedParamType).Append(")sp.GetService(").Append(serviceTypeExpr).Append(")!");
                    }
                    else
                    {
                        if (param.IsOptional)
                            sb.Append('(').Append(param.FullyQualifiedTypeName).Append("?)sp.GetService(typeof(").Append(param.FullyQualifiedTypeName).Append("))");
                        else
                            sb.Append('(').Append(param.FullyQualifiedTypeName).Append(")sp.GetService(typeof(").Append(param.FullyQualifiedTypeName).Append("))!");
                    }
                }
            }
            sb.Append(')');
            return sb.ToString();
        }

        private static string BuildTypeofArgs(int arity)
        {
            if (arity == 1) return "typeof(T)";
            var sb = new System.Text.StringBuilder();
            for (int i = 1; i <= arity; i++)
            {
                if (i > 1) sb.Append(", ");
                sb.Append("typeof(T").Append(i).Append(')');
            }
            return sb.ToString();
        }

        private const string OgDelegateCreator =
            "static (t, mi) => (global::System.Func<global::System.IServiceProvider, object>)" +
            "global::System.Delegate.CreateDelegate(" +
            "typeof(global::System.Func<global::System.IServiceProvider, object>), " +
            "mi.MakeGenericMethod(t.GetGenericArguments()))";

        /// <summary>Emits the open-generic if-chain into ResolveKnown (root provider).</summary>
        private static void EmitOpenGenericRootResolve(
            StringBuilder sb,
            List<ServiceRegistrationInfo> openGenerics,
            string indent,
            string className)
        {
            sb.AppendLine(indent + "if (serviceType.IsGenericType)");
            sb.AppendLine(indent + "{");
            sb.AppendLine(indent + "    var _genDef = serviceType.GetGenericTypeDefinition();");
            for (int ogIdx = 0; ogIdx < openGenerics.Count; ogIdx++)
            {
                var svc = openGenerics[ogIdx];
                if (svc.Key != null) continue;
                int arity = int.Parse(svc.OpenGenericArity!);
                var ifaces = svc.AsType != null ? new List<string> { svc.AsType } : svc.Interfaces;
                foreach (var iface in ifaces)
                {
                    var openIface = ToUnboundGenericString(iface, arity);
                    sb.AppendLine(indent + "    if (_genDef == typeof(" + openIface + "))");
                    sb.AppendLine(indent + "    {");
                    if (string.Equals(svc.Lifetime, "Scoped", StringComparison.Ordinal))
                    {
                        sb.AppendLine(indent + "        return null; // scoped open generics not resolved from root");
                    }
                    else if (string.Equals(svc.Lifetime, "Singleton", StringComparison.Ordinal))
                    {
                        sb.AppendLine(indent + "        var _d_" + ogIdx + " = _og_dc_" + ogIdx + ".GetOrAdd(serviceType, " + OgDelegateCreator + ", _og_mi_" + ogIdx + ");");
                        sb.AppendLine(indent + "        return _og_sc_" + ogIdx + ".GetOrAdd(serviceType, (t, state) => state.d(state.sp), (d: _d_" + ogIdx + ", sp: (global::System.IServiceProvider)this));");
                    }
                    else // Transient
                    {
                        sb.AppendLine(indent + "        var _d_" + ogIdx + " = _og_dc_" + ogIdx + ".GetOrAdd(serviceType, " + OgDelegateCreator + ", _og_mi_" + ogIdx + ");");
                        sb.AppendLine(indent + "        return _d_" + ogIdx + "(this);");
                    }
                    sb.AppendLine(indent + "    }");
                }
            }
            sb.AppendLine(indent + "}");
        }

        /// <summary>Emits the open-generic if-chain into ResolveScopedKnown (scope).</summary>
        private static void EmitOpenGenericScopeResolve(
            StringBuilder sb,
            List<ServiceRegistrationInfo> openGenerics,
            string indent,
            string className)
        {
            sb.AppendLine(indent + "if (serviceType.IsGenericType)");
            sb.AppendLine(indent + "{");
            sb.AppendLine(indent + "    var _genDef = serviceType.GetGenericTypeDefinition();");
            for (int ogIdx = 0; ogIdx < openGenerics.Count; ogIdx++)
            {
                var svc = openGenerics[ogIdx];
                if (svc.Key != null) continue;
                int arity = int.Parse(svc.OpenGenericArity!);
                var ifaces = svc.AsType != null ? new List<string> { svc.AsType } : svc.Interfaces;
                foreach (var iface in ifaces)
                {
                    var openIface = ToUnboundGenericString(iface, arity);
                    sb.AppendLine(indent + "    if (_genDef == typeof(" + openIface + "))");
                    sb.AppendLine(indent + "    {");
                    if (string.Equals(svc.Lifetime, "Singleton", StringComparison.Ordinal))
                    {
                        sb.AppendLine(indent + "        return Root.GetService(serviceType);");
                    }
                    else if (string.Equals(svc.Lifetime, "Scoped", StringComparison.Ordinal))
                    {
                        sb.AppendLine(indent + "        return GetOrAddScopedOpenGeneric(serviceType, () =>");
                        sb.AppendLine(indent + "        {");
                        sb.AppendLine(indent + "            var _d_" + ogIdx + " = " + className + "._og_dc_" + ogIdx + ".GetOrAdd(serviceType, " + OgDelegateCreator + ", " + className + "._og_mi_" + ogIdx + ");");
                        sb.AppendLine(indent + "            return _d_" + ogIdx + "(this);");
                        sb.AppendLine(indent + "        });");
                    }
                    else // Transient
                    {
                        sb.AppendLine(indent + "        var _d_" + ogIdx + " = " + className + "._og_dc_" + ogIdx + ".GetOrAdd(serviceType, " + OgDelegateCreator + ", " + className + "._og_mi_" + ogIdx + ");");
                        sb.AppendLine(indent + "        return _d_" + ogIdx + "(this);");
                    }
                    sb.AppendLine(indent + "    }");
                }
            }
            sb.AppendLine(indent + "}");
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
                    ctorParams.Add(new ConstructorParameterInfo(matchFqn, param.Name, param.HasExplicitDefaultValue));
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
                isOpenGeneric, ctorParams, implementsDisposable, isAbstractOrStatic);
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
