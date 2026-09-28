using System.Runtime.InteropServices;

namespace ZeroAlloc.Inject.AotSmoke;

/// <summary>A user struct used as a closed generic argument, directly and as a nullable.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct SmokeKey(int Id, int Part);
