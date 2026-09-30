using System;
using ZeroAlloc.Inject;

namespace ZeroAlloc.Inject.AotSmoke;

// Non-generic decorators of a singleton, of a scoped service with a disposable inner, and of a
// service registered with As. Every mode resolves the same decorated instance, and disposes the
// decorator and the inner it wraps, as Microsoft DI does.
public interface IAuditLog
{
    bool IsDisposed { get; }
}

[Singleton]
public sealed class AuditLog : IAuditLog, IDisposable
{
    public bool IsDisposed { get; private set; }
    public void Dispose() => IsDisposed = true;
}

[Decorator]
public sealed class TimestampedAuditLog : IAuditLog, IDisposable
{
    public TimestampedAuditLog(IAuditLog inner) => Inner = inner;

    public IAuditLog Inner { get; }
    public bool IsDisposed { get; private set; }
    public void Dispose() => IsDisposed = true;
}

public interface IUnitOfWork
{
    bool IsDisposed { get; }
}

[Scoped]
public sealed class UnitOfWork : IUnitOfWork, IDisposable
{
    public bool IsDisposed { get; private set; }
    public void Dispose() => IsDisposed = true;
}

[Decorator]
public sealed class TracedUnitOfWork : IUnitOfWork
{
    public TracedUnitOfWork(IUnitOfWork inner) => Inner = inner;

    public IUnitOfWork Inner { get; }
    public bool IsDisposed => Inner.IsDisposed;
}

public interface IReportSink
{
    string Name { get; }
}

public interface IOtherSink
{
}

[Scoped(As = typeof(IReportSink))]
public sealed class ReportSink : IReportSink, IOtherSink
{
    public string Name => "sink";
}

[Decorator]
public sealed class BufferedReportSink : IReportSink
{
    public BufferedReportSink(IReportSink inner) => Inner = inner;

    public IReportSink Inner { get; }
    public string Name => $"buffered({Inner.Name})";
}
