; Shipped analyzer releases.
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

## Release 0.11.3

### New Rules

Rule ID | Category         | Severity | Notes
--------|------------------|----------|---------------------------------------------------------------------
ZAI001  | ZeroAlloc.Inject | Error    | Multiple lifetime attributes
ZAI002  | ZeroAlloc.Inject | Error    | Attribute on non-class type
ZAI003  | ZeroAlloc.Inject | Error    | Attribute on abstract or static class
ZAI004  | ZeroAlloc.Inject | Error    | As type not implemented
ZAI005  | ZeroAlloc.Inject | Error    | Keyed services require .NET 8+
ZAI006  | ZeroAlloc.Inject | Warning  | No public constructor
ZAI007  | ZeroAlloc.Inject | Warning  | No interfaces implemented
ZAI008  | ZeroAlloc.Inject | Warning  | Missing DI abstractions
ZAI009  | ZeroAlloc.Inject | Error    | Multiple public constructors without [ActivatorUtilitiesConstructor]
ZAI010  | ZeroAlloc.Inject | Error    | Constructor parameter is a primitive/value type
ZAI011  | ZeroAlloc.Inject | Error    | Decorator has no matching interface
ZAI012  | ZeroAlloc.Inject | Error    | Decorator inner service not found
ZAI013  | ZeroAlloc.Inject | Warning  | Decorator on abstract or static class
ZAI014  | ZeroAlloc.Inject | Error    | Circular dependency detected
ZAI015  | ZeroAlloc.Inject | Error    | [OptionalDependency] on non-nullable parameter
ZAI016  | ZeroAlloc.Inject | Error    | [DecoratorOf] interface not implemented
ZAI017  | ZeroAlloc.Inject | Error    | Duplicate decorator Order
ZAI018  | ZeroAlloc.Inject | Warning  | No closed usages detected for open generic

## Release 1.2.0

### New Rules

Rule ID | Category         | Severity | Notes
--------|------------------|----------|----------------------------------
ZAI019  | ZeroAlloc.Inject | Error    | [Inject] on non-settable property

## Release 1.8.0

### New Rules

Rule ID | Category         | Severity | Notes
--------|------------------|----------|---------------------------------------------
ZAI020  | ZeroAlloc.Inject | Error    | Invalid ZeroAllocGeneratedAccessibility value

## Release 1.9.0

### Removed Rules

Rule ID | Category         | Severity | Notes
--------|------------------|----------|--------------------------------------------------------------------
ZAI002  | ZeroAlloc.Inject | Error    | Attribute on non-class type, redundant with compiler error CS0592
ZAI005  | ZeroAlloc.Inject | Error    | Keyed services require .NET 8+, unreachable: the package needs net8.0
