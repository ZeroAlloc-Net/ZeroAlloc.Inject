using System;
using ZeroAlloc.Inject;

namespace ZeroAlloc.Inject.AotSmoke;

// Disposable transients resolved from the root provider, a plain one and an open generic closed
// over a value type. Every mode disposes them with the provider, as Microsoft DI does.
public interface IDisposalProbe
{
    bool IsDisposed { get; }
}

[Transient]
public sealed class DisposalProbe : IDisposalProbe, IDisposable
{
    public bool IsDisposed { get; private set; }
    public void Dispose() => IsDisposed = true;
}

public interface IGenericDisposalProbe<T>
{
    bool IsDisposed { get; }
}

[Transient]
public sealed class GenericDisposalProbe<T> : IGenericDisposalProbe<T>, IDisposable
{
    public bool IsDisposed { get; private set; }
    public void Dispose() => IsDisposed = true;
}

// Stub: surfaces IGenericDisposalProbe<int> for the generator's closed-usage scan.
[Transient]
internal sealed class DisposalProbeUsage
{
    public DisposalProbeUsage(IGenericDisposalProbe<int> _) { }
}

// A decorated open generic transient: the root disposes the decorator and the concrete inner it wraps.
public interface IDecoratedDisposalProbe<T>
{
    bool IsDisposed { get; }
}

[Transient]
public sealed class DecoratedDisposalProbe<T> : IDecoratedDisposalProbe<T>, IDisposable
{
    public bool IsDisposed { get; private set; }
    public void Dispose() => IsDisposed = true;
}

[Decorator]
public sealed class AuditedDisposalProbe<T> : IDecoratedDisposalProbe<T>, IDisposable
{
    public AuditedDisposalProbe(IDecoratedDisposalProbe<T> inner) => Inner = inner;

    public IDecoratedDisposalProbe<T> Inner { get; }
    public bool IsDisposed { get; private set; }
    public void Dispose() => IsDisposed = true;
}

// Stub: surfaces IDecoratedDisposalProbe<int> for the generator's closed-usage scan.
[Transient]
internal sealed class DecoratedDisposalProbeUsage
{
    public DecoratedDisposalProbeUsage(IDecoratedDisposalProbe<int> _) { }
}
