using ZeroAlloc.Inject;

namespace ZeroAlloc.Inject.AotSmoke;

public interface IHttpRequestContext
{
    int RequestId { get; }
}

[Scoped]
public sealed class HttpRequestContext : IHttpRequestContext
{
    private static int _next;
    public HttpRequestContext()
    {
        RequestId = System.Threading.Interlocked.Increment(ref _next);
    }
    public int RequestId { get; }
}
