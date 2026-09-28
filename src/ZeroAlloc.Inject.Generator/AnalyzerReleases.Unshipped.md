; Unshipped analyzer release.
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### Removed Rules

Rule ID | Category         | Severity | Notes
--------|------------------|----------|--------------------------------------------------------------------
ZAI002  | ZeroAlloc.Inject | Error    | Attribute on non-class type, redundant with compiler error CS0592
ZAI005  | ZeroAlloc.Inject | Error    | Keyed services require .NET 8+, unreachable: the package needs net8.0
