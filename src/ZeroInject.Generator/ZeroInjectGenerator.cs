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

            var combined = transients
                .Combine(scopeds)
                .Combine(singletons)
                .Combine(assemblyAttr)
                .Combine(assemblyName)
                .Combine(hasContainer);

            context.RegisterSourceOutput(combined, static (spc, data) =>
            {
                var transientInfos = data.Left.Left.Left.Left.Left;
                var scopedInfos = data.Left.Left.Left.Left.Right;
                var singletonInfos = data.Left.Left.Left.Right;
                var methodNameOverrides = data.Left.Left.Right;
                var asmName = data.Left.Right;
                var containerReferenced = data.Right;

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

                if (allServices.Count == 0)
                {
                    return;
                }

                string? methodNameOverride = null;
                if (methodNameOverrides.Length > 0)
                {
                    methodNameOverride = methodNameOverrides[0];
                }

                var source = GenerateExtensionClass(allServices, asmName, methodNameOverride);
                spc.AddSource("ZeroInject.ServiceCollectionExtensions.g.cs", source);

                if (containerReferenced)
                {
                    var providerSource = GenerateServiceProviderClass(allServices, asmName);
                    spc.AddSource("ZeroInject.ServiceProvider.g.cs", providerSource);
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
                primitiveParameterType);
        }

        private static string GenerateExtensionClass(
            List<ServiceRegistrationInfo> services,
            string assemblyName,
            string? methodNameOverride)
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
                EmitRegistration(sb, svc);
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

        private static void EmitRegistration(StringBuilder sb, ServiceRegistrationInfo svc)
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

            // Register all non-filtered interfaces
            foreach (var iface in svc.Interfaces)
            {
                EmitSingleRegistration(sb, lifetime, iface, fqn, svc.Key, useAdd, svc.IsOpenGeneric, svc.ConstructorParameters);
            }

            // Always register concrete type
            EmitConcreteRegistration(sb, lifetime, fqn, svc.Key, useAdd, svc.IsOpenGeneric, svc.ConstructorParameters);
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
                sb.AppendLine(string.Format(
                    "            services.{0}<{1}>(\"{2}\", {3});",
                    method, serviceType, key, factory));
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

        private static string GenerateServiceProviderClass(
            List<ServiceRegistrationInfo> services,
            string assemblyName)
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

            foreach (var svc in services)
            {
                if (svc.IsOpenGeneric) continue;
                if (svc.Lifetime == "Transient") transients.Add(svc);
                else if (svc.Lifetime == "Singleton") singletons.Add(svc);
                else if (svc.Lifetime == "Scoped") scopeds.Add(svc);
            }

            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated />");
            sb.AppendLine("#nullable enable");
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Threading;");
            sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
            sb.AppendLine();
            sb.AppendLine("namespace ZeroInject.Generated");
            sb.AppendLine("{");
            sb.AppendLine("    internal sealed class " + className + " : global::ZeroInject.Container.ZeroInjectServiceProviderBase");
            sb.AppendLine("    {");

            // Singleton fields
            for (int i = 0; i < singletons.Count; i++)
            {
                sb.AppendLine("        private " + singletons[i].FullyQualifiedName + "? _singleton_" + i + ";");
            }
            if (singletons.Count > 0)
            {
                sb.AppendLine();
            }

            // Constructor
            sb.AppendLine("        public " + className + "(IServiceProvider fallback) : base(fallback) { }");
            sb.AppendLine();

            // ResolveKnown - root provider: transients + singletons (scoped returns null)
            sb.AppendLine("        protected override object? ResolveKnown(Type serviceType)");
            sb.AppendLine("        {");

            // Transients
            foreach (var svc in transients)
            {
                var newExpr = BuildNewExpression(svc);
                EmitTypeChecks(sb, svc, newExpr, "            ");
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
                    sb.AppendLine("            if (serviceType == typeof(" + serviceType + "))");
                    sb.AppendLine("            {");
                    sb.AppendLine("                if (" + fieldName + " != null) return " + fieldName + ";");
                    sb.AppendLine("                var instance = " + newExpr + ";");
                    sb.AppendLine("                return Interlocked.CompareExchange(ref " + fieldName + ", instance, null) ?? " + fieldName + ";");
                    sb.AppendLine("            }");
                }
            }

            sb.AppendLine("            return null;");
            sb.AppendLine("        }");
            sb.AppendLine();

            // CreateScopeCore
            sb.AppendLine("        protected override global::ZeroInject.Container.ZeroInjectScope CreateScopeCore(IServiceScope fallbackScope)");
            sb.AppendLine("        {");
            sb.AppendLine("            return new Scope(this, fallbackScope);");
            sb.AppendLine("        }");
            sb.AppendLine();

            // Nested Scope class
            sb.AppendLine("        private sealed class Scope : global::ZeroInject.Container.ZeroInjectScope");
            sb.AppendLine("        {");

            // Scoped fields
            for (int i = 0; i < scopeds.Count; i++)
            {
                sb.AppendLine("            private " + scopeds[i].FullyQualifiedName + "? _scoped_" + i + ";");
            }
            if (scopeds.Count > 0)
            {
                sb.AppendLine();
            }

            // Scope constructor
            sb.AppendLine("            public Scope(" + className + " root, IServiceScope fallbackScope) : base(root, fallbackScope) { }");
            sb.AppendLine();

            // ResolveScopedKnown
            sb.AppendLine("            protected override object? ResolveScopedKnown(Type serviceType)");
            sb.AppendLine("            {");

            // Transients in scope - fresh instance each call
            foreach (var svc in transients)
            {
                var newExpr = BuildNewExpressionForScope(svc);
                EmitTypeChecks(sb, svc, newExpr, "                ");
            }

            // Singletons in scope - delegate to Root
            foreach (var svc in singletons)
            {
                var serviceTypes = GetServiceTypes(svc);
                foreach (var serviceType in serviceTypes)
                {
                    sb.AppendLine("                if (serviceType == typeof(" + serviceType + "))");
                    sb.AppendLine("                    return Root.GetService(serviceType);");
                }
            }

            // Scoped services
            for (int i = 0; i < scopeds.Count; i++)
            {
                var svc = scopeds[i];
                var fieldName = "_scoped_" + i;
                var newExpr = BuildNewExpressionForScope(svc);
                var serviceTypes = GetServiceTypes(svc);

                foreach (var serviceType in serviceTypes)
                {
                    sb.AppendLine("                if (serviceType == typeof(" + serviceType + "))");
                    sb.AppendLine("                {");
                    sb.AppendLine("                    if (" + fieldName + " == null) " + fieldName + " = " + newExpr + ";");
                    sb.AppendLine("                    return " + fieldName + ";");
                    sb.AppendLine("                }");
                }
            }

            sb.AppendLine("                return null;");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            return sb.ToString();
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

        private static void EmitTypeChecks(StringBuilder sb, ServiceRegistrationInfo svc, string newExpr, string indent)
        {
            var serviceTypes = GetServiceTypes(svc);
            foreach (var serviceType in serviceTypes)
            {
                sb.AppendLine(indent + "if (serviceType == typeof(" + serviceType + "))");
                sb.AppendLine(indent + "    return " + newExpr + ";");
            }
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
                sb.AppendLine(string.Format(
                    "            services.{0}(\"{1}\", {2});",
                    method, key, factory));
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
    }
}
