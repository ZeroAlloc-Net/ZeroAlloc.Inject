using System;
using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Inject.AotSmoke;

// Exercise the generator-emitted AddZeroAllocInjectAotSmokeServices() extension
// under PublishAot=true. The DI container must:
//   1. Instantiate Greeter (Singleton, no deps) once and share it
//   2. Instantiate WelcomeService (Transient) and wire IGreeter into its ctor
//   3. Return the expected composed greeting

var services = new ServiceCollection();
services.AddZeroAllocInjectAotSmokeServices();
using var provider = services.BuildServiceProvider();

var welcome = provider.GetRequiredService<IWelcomeService>();
var greeting = welcome.WelcomeUser("AOT");
if (!string.Equals(greeting, "Hello, AOT!", StringComparison.Ordinal))
    return Fail($"WelcomeUser expected 'Hello, AOT!', got '{greeting}'");

// Singleton identity — two resolutions of IGreeter share the same instance
var g1 = provider.GetRequiredService<IGreeter>();
var g2 = provider.GetRequiredService<IGreeter>();
if (!ReferenceEquals(g1, g2))
    return Fail("IGreeter Singleton: two resolutions should share one instance");

// Transient identity — two resolutions of IWelcomeService are distinct
var w1 = provider.GetRequiredService<IWelcomeService>();
var w2 = provider.GetRequiredService<IWelcomeService>();
if (ReferenceEquals(w1, w2))
    return Fail("IWelcomeService Transient: two resolutions should be distinct");

// Scoped lifetime — same instance within a scope, distinct across scopes.
using (var scope1 = provider.CreateScope())
{
    var c1a = scope1.ServiceProvider.GetRequiredService<IHttpRequestContext>();
    var c1b = scope1.ServiceProvider.GetRequiredService<IHttpRequestContext>();
    if (!ReferenceEquals(c1a, c1b))
        return Fail($"IHttpRequestContext Scoped: two resolutions in same scope should share instance (got RequestId {c1a.RequestId} vs {c1b.RequestId})");

    using var scope2 = provider.CreateScope();
    var c2 = scope2.ServiceProvider.GetRequiredService<IHttpRequestContext>();
    if (ReferenceEquals(c1a, c2))
        return Fail($"IHttpRequestContext Scoped: resolutions in different scopes should be distinct (both got RequestId {c1a.RequestId})");
}

// Decorator pattern — resolving IFoo returns LoggingFoo wrapping Foo.
var foo = provider.GetRequiredService<IFoo>();
if (foo is not LoggingFoo)
    return Fail($"IFoo Decorator: expected LoggingFoo, got {foo.GetType().Name}");
var fooResult = foo.DoStuff("x");
if (!string.Equals(fooResult, "[logged] base:x", StringComparison.Ordinal))
    return Fail($"IFoo Decorator: expected '[logged] base:x', got '{fooResult}'");

Console.WriteLine("AOT smoke: PASS");
return 0;

static int Fail(string message)
{
    Console.Error.WriteLine($"AOT smoke: FAIL — {message}");
    return 1;
}
