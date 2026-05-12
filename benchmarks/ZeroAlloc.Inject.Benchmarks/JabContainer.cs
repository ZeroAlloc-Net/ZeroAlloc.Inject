using Jab;

namespace ZeroAlloc.Inject.Benchmarks;

// Jab service provider mirroring the same service graph as the ZA and MS DI
// harnesses. Notes on parity:
//
//  - PropertyInjection: Jab is constructor-only, so IServiceWithPropertyDep is
//    excluded. The PropertyInjection scenario keeps reporting MS DI / ZA only.
//  - Decorator: Jab has no first-class decorator attribute. Modeled as a
//    factory binding that wires the LoggingDecoratedService around
//    DecoratedServiceImpl, matching the runtime shape of MS DI's binding.
//  - Open generics: Jab supports open generics; the closed IGenericRepo<string>
//    registration is sufficient for our benchmark.
[ServiceProvider]
[Transient<ISimpleService, SimpleService>]
[Transient<IServiceWithDep, ServiceWithDep>]
[Transient<IServiceWithMultipleDeps, ServiceWithMultipleDeps>]
[Singleton<ISingletonService, SingletonService>]
[Scoped<IScopedService, ScopedService>]
[Transient<IMultiService, MultiServiceA>]
[Transient<IMultiService, MultiServiceB>]
[Transient<IMultiService, MultiServiceC>]
[Transient<DecoratedServiceImpl>]
[Transient<IDecoratedService>(Factory = nameof(BuildDecorated))]
public sealed partial class JabContainer
{
    // Open generics: Jab requires closed-type registration at the provider
    // attribute level (no [Transient(typeof(IGenericRepo<>), typeof(GenericRepo<>))]
    // syntax in 0.10.x), so IGenericRepo<T> is intentionally absent. The
    // OpenGeneric benchmark scenario keeps reporting MS DI + ZA only.
    //
    // PropertyInjection: Jab is constructor-only — IServiceWithPropertyDep
    // is intentionally absent.
    private IDecoratedService BuildDecorated()
        => new LoggingDecoratedService(GetService<DecoratedServiceImpl>());
}
