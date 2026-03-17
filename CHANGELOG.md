# Changelog

## [1.2.0](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/compare/v1.1.0...v1.2.0) (2026-03-17)


### Features

* add [Inject] attribute for property injection ([d752e76](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/d752e76e45b6b15635af44d4bc291a530906e665))
* add [Inject] attribute for property injection ([142b75e](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/142b75ef6d19c3210ec73034d1ed0d44bf7cf996))
* add PropertyInjectionInfo model ([14d024d](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/14d024d753beb7ea29a9c53674cb306a68952114))
* add PropertyInjections and NonSettableInjectProperties to ServiceRegistrationInfo ([2e2c1f5](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/2e2c1f5a88d6110364b05bc790cd7af44205c703))
* add ZAI019 diagnostic for [Inject] on non-settable property ([c212e01](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/c212e015f3c7c2a4a23facbcc080c951bb9bc865))
* emit block lambda when property injections are present ([62fbc37](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/62fbc3719e2fa57593b01737811cb31f2b407556))
* scan [Inject] properties in GetServiceInfo, emit ZAI019 ([7f56e54](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/7f56e54f3417e0916b011fe404e898e456a33bea))


### Bug Fixes

* apply property injection setters in standalone provider ResolveKnown ([f66e5fb](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/f66e5fb2908d399953a6c0c605cdc9d07c84efb4))
* reject init-only setters for [Inject] and apply property injection in keyed standalone fast-path ([1a567fe](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/1a567feb1810f43fe9f3a7c7cef3a49ec18deb1e))

## [1.1.0](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/compare/v1.0.0...v1.1.0) (2026-03-16)


### Features

* add [DecoratorOf] and [OptionalDependency] attributes ([d255080](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/d255080d8cac0faac2bbc2edb75638c7122049f8))
* add [DecoratorOf] pipeline to generator ([e47cf06](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/e47cf06aebdda632b782c3ca174e6eb49f1121b6))
* add [OptionalDependency] attribute with ZI015 diagnostic ([196ea38](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/196ea38cd1882926da0072fce3ed6cf4f92a6f7f))
* add benchmarks with IEnumerable and scope creation scenarios ([cd7222b](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/cd7222bd4fe50663b6884f5c64073aff9af453c9))
* add compile-time diagnostics ZI006 and ZI007 ([8370e95](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/8370e95c74b870e2c77d6cc134f73401ada5896c))
* add constructor analysis and factory lambda emission in GetServiceInfo ([184159c](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/184159cea0a0f84040fb742044da3c917c2e17fa))
* add constructor injection example to sample app ([741a5bd](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/741a5bd2fba8e8478749747cf18f479412a72b24))
* add ConstructorParameterInfo data model and extend ServiceRegistrationInfo ([1d97be4](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/1d97be40a302b1520841efc0aefc3e5a9e89d9d7))
* add core service lifetime attributes and ZeroInject assembly attribute ([2ea5acd](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/2ea5acdef993071dc9f7c220e325f00efbcd3b46))
* add DecoratorAttribute to ZeroInject package ([e126ff7](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/e126ff7317ceb7b22a2ba5abf689a254c3771bf4))
* add DecoratorOfAttribute and extend DecoratorRegistrationInfo with Order/WhenRegisteredFqn/IsDecoratorOf ([f8ddc4a](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/f8ddc4a09ce1c959a92d5563507f6f95911dfa3a))
* add DecoratorRegistrationInfo, ZI011-ZI013 diagnostics, and generator pipeline wiring ([6f42647](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/6f426470578cd44f27b55350ef6d736e0d02520f))
* add FindClosedGenericUsages analysis pass for AOT open generic discovery ([35aa149](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/35aa14950334d5d8412f84e7a1e67a735a7f0a78))
* add generator skeleton, test infrastructure, data model, and diagnostic descriptors ([aeee61f](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/aeee61f9fcff4ff14e454c15c125a7d8952c2a82))
* add IEnumerable&lt;T&gt; support, blindspot fixes, and thread-safe scope disposal ([e6edcf7](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/e6edcf76a74e7e70c877f10bbd9d329d0f09da96))
* add net9.0 target and fix conditional MS DI references ([ee74ce8](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/ee74ce866a5b1c6eb679f93866a2cc5b7e95bc72))
* add OpenGenericEntry and runtime open-generic resolution to standalone base classes ([f28c6b2](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/f28c6b200da473ae619321ecbb189dced459216c))
* add standalone provider benchmarks for resolution and registration ([d9b6c5b](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/d9b6c5bd68a24888b2339d52286cba61bf0d91d3))
* add ZeroInject sample application ([da25917](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/da2591765fab4077225a9ac5f6e49b52c6698762))
* add ZeroInject.Container runtime base classes ([7b75997](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/7b759976884dbdf3d47d9db61ffef6da729d5a5f))
* add ZeroInjectStandaloneProvider and ZeroInjectStandaloneScope base classes ([0080be9](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/0080be9e74d1a1e6722b8d9b0b85eb341afd6b70))
* add ZI009 and ZI010 diagnostics for constructor validation ([cec517f](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/cec517fa2c7f7047b0cf1ddc702a1ed32295d130))
* add ZI018 warning when open generic has no detected closed usages ([6b3dda6](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/6b3dda61b9604590b36c515207adb7fb1e7b0070))
* code-gen open-generic delegate factories for ~6x faster standalone resolution ([5fa46fb](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/5fa46fb0ae357c4ca29ebb2c792cb16af36960a6))
* compile-time circular dependency detection (ZI014) ([39577b2](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/39577b25671fe4ead63a4399c5e9c8edc603f2cf))
* emit decorator wrapping and open-generic map in all generator outputs ([bfbeb32](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/bfbeb32e972ec62dca2683f111adb3fbd664ef72))
* emit Dispose/DisposeAsync overrides for standalone provider singletons ([9c6afec](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/9c6afec5483fe97b4b1385dab596c55d5f470abb))
* emit explicit closed generic entries in standalone container ResolveKnown/ResolveScoped ([5570360](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/5570360e232ce5aeda7c9db7f8ff80be020e0c3a))
* emit factory lambdas for keyed registrations ([9817583](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/98175836aed3ecf2cfadc8d7667a0d7e459c9784))
* emit IEnumerable&lt;T&gt; checks in scope ResolveScopedKnown ([1dd3ac7](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/1dd3ac7d2a03a6dd5d5e3df7855a363a1a6ce235))
* emit IsKnownService override from generator ([a96a1ff](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/a96a1ff3652536282c9d9fb2e26e952959913875))
* emit standalone provider class from generator ([6132574](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/613257452eaa3cc7f42ff8b9d1fb448703940a2f))
* emit WhenRegistered conditional guard in AddXxxServices() for [DecoratorOf] ([72a8a7c](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/72a8a7cffcd367d2c54b5028b2f42af023946372))
* extend data models with open generic metadata for AOT analysis ([a5da590](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/a5da590b5d7e75f035721bcb3f6d03646afdde6e))
* generate BuildZeroInjectServiceProvider and ZeroInjectServiceProviderFactory ([7ba83a8](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/7ba83a85a15afc3b11e3fb1a67d10544d0c69b88))
* generate IServiceProvider with transient, singleton, and scoped resolution ([fedfc95](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/fedfc955ac7b6130d914581ab4ddecaf1ee2f438))
* implement core source generator with basic service discovery and registration ([70d8d1e](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/70d8d1ed94859d3199426544260731e4a73f3ef8))
* implement IServiceProviderIsKeyedService on all base classes and scopes ([aa01830](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/aa018304b662f75dcbb12d961649778452efdee0))
* implement IServiceProviderIsService on all base classes ([64b30df](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/64b30dfb81ad5b142b8c4f7b1ab75b797462560f))
* integration test and README for DecoratorOf/OptionalDependency ([811433f](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/811433f5e31b4e5beb7a729d86338bad20f064b2))
* Native AOT — compile-time closed generic enumeration ([b0d3576](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/b0d35769d48d14d3e542c0b83d148836b26fecb7))
* remove MakeGenericType open generic machinery — standalone container is now reflection-free ([c7881df](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/c7881dfba70b68c55421bfe8c9008f9387b234b0))
* scaffold ZeroInject solution structure ([521051c](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/521051c92e6090ac0ee36bee1229aa701b6277be))
* sort decorators by Order and emit ZI017 for duplicate Order ([ae63a3f](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/ae63a3ffdb5eb347efb09706977470681e5b317b))
* standalone container, production features, rebrand to ZInject ([4b65c9f](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/4b65c9ff6f84069a5a80a511c247f4618bcc909b))
* support multi-decorator stacking with chained wrapping ([e9ff721](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/e9ff7211a0994e66eda55022a4d6b961434cd9a3))


### Bug Fixes

* add cancellation token check and correct open generic TryAdd semantics ([9e421e0](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/9e421e0f49007b5a1e1010d071c4bf588816b8c0))
* add IDisposable disposal-on-race for singleton closed generics, cache scoped closed generics per scope ([d389a29](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/d389a29cc5916eb3fc6b15d8948c12bc4f0694cd))
* add missing IsKeyedService type check in generator and add generator tests ([968dc7a](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/968dc7a15040f2c6012e85280260abf7a4d4db08))
* address code review issues for keyed services and equality ([6b89c18](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/6b89c18a6dbaf73aab87da91570663f302db1ffb))
* align UnboundGenericInterfaceFqn format with Interfaces FQN format ([1f09a28](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/1f09a28b4cb39aa9950ba100686aabe2a5041374))
* apply open-generic decorator chain in standalone closed-type entries ([1e0020a](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/1e0020a07a54c11f7ae3e92ebcc9e5ec6d1d9f68))
* bundle generator DLL in ZInject.Container NuGet package ([630d455](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/630d455a0fe29698021489c2e23691b75f818518))
* cache keyed singletons and add null-argument validation ([15a71f3](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/15a71f32794d6380d781e6a2931f47ebe2ff4d6a))
* chain publish job inside release-please workflow ([9f14c6a](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/9f14c6a3554190e6869a95aa7ed2e40b287ce48b))
* chain publish job inside release-please workflow ([bd3d934](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/bd3d9345575b9fa455d0689e529f6172dc9dc1e9))
* clarify ordering assertion comment in integration test ([6c0fd59](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/6c0fd59d0d3f3998d20345bd08990b4ce4c08437))
* consolidate constructor iteration and complete equality implementations ([748d51f](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/748d51fb761761f1744338c603239a73bdca2ebc))
* correct remaining ZInject references in CHANGELOG, Directory.Build.props, README, and test comments ([c6d2ef1](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/c6d2ef1ae86248291c4870557616a863a3196ab0))
* dispose closed-generic singleton fields in generated container Dispose/DisposeAsync ([400e34d](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/400e34ded46e36b79d03f543c943d859268d6d37))
* emit 'using System.Linq;' in generated file when WhenRegistered guard is used ([47e05ca](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/47e05ca817fae203e53a3f0f15d57d73d71cb57e))
* emit ZI016 for [DecoratorOf] interface not implemented, not ZI011 ([91334dc](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/91334dcff30460427d9b719a19c17b28222cfa55))
* GetService returns last registration (last-wins, matching MS DI) ([fdf04e5](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/fdf04e5cec5f483ac172eb1180c098d208e8ae8e))
* improve GetHashCode for TypeArgumentMetadataNames, fix namespace style ([10c3613](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/10c3613e2b718118b34459d7e0b2f3795e73e60a))
* include Order and IsDecoratorOf in DecoratorRegistrationInfo.GetHashCode ([189932e](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/189932e85b29f42f2e7dbccb441f6b8afd54d335))
* pack generator DLL into analyzers/dotnet/cs ([355bdc3](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/355bdc3156eb39ca2613470d2c3d8172d3b45658))
* pack generator DLL into analyzers/dotnet/cs for NuGet packages ([83e67d1](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/83e67d195080856096f1fbaa772a79cf6f56534c))
* pack generator DLL into analyzers/dotnet/cs instead of lib ([3667d9e](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/3667d9ebfa32b3bbb0a757c21dcbdf6568aff81b))
* resolve all analyzer warnings ([d9f0532](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/d9f053202b3385d64d2c2d2c5013062c7cbc6240))
* resolve Container analyzer DLL path reliably ([ee52f28](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/ee52f280d9fb782985a2704e1e1a49fdef9c61ee))
* sort decorator Order ascending so Order=1 is innermost (closest to implementation) ([3e054d2](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/3e054d2cdf7630f49d14a7a389e937b41d681ce7))
* trigger publish on tag push + add GitHub Packages ([df26ab0](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/df26ab0816e0c08539debc921463b6067e607783))
* trigger publish workflow on push to main ([4834b63](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/4834b637cd564d5ed71887ef34cd69432e14f32f))
* trigger publish workflow on version tags only ([d7744c1](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/d7744c1e6427408f20cf81dd4608ca05ad1d9b87))
* trigger publish workflow on version tags only ([571d171](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/571d17133b2a9fd1fd0fed67088f55a8ebc2256b))
* update GitHub Packages source URL to ZeroAlloc-Net org ([a500441](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/a500441c1ac4d2a6a7d9b06e161d02bc6d4f904f))
* update GitHub Packages source URL to ZeroAlloc-Net org ([92ef6e5](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/92ef6e520511d143a87c88a999c0188eea33b05c))
* update GitVersion.yml to v6 format ([4f62800](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/4f628004d32aae7e8b9b047004fc2ed0c3111e19))
* update GitVersion.yml to v6 format ([e1471cb](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/e1471cb12e5b34c00cc7688dba60350b9ba08557))
* update MethodNamingTests to use ZeroAllocInject attribute ([aad9877](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/aad987782284cff47361e5203e8e9ab6dde2a66e))
* update remaining ZInject references in benchmark descriptions and method names ([aa6028f](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/aa6028f4af74b24650e94f71618ffbab3e4d0043))
* use forward slashes in csproj paths for cross-platform builds ([0c24beb](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/0c24bebb576f45cfc2dea07a11eef47c2214484f))
* use MSBuildThisFileDirectory for generator DLL path in Container package ([b5d3563](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/b5d3563a4ad81dccee746538c11ae27b0a839bf1))
* use nullable object? for IsKnownKeyedService parameter ([2047324](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/2047324806be4eadc7e61546b159eaf6ab2c3dcc))
* ZI015 false positive in nullable-disabled projects, cache GetAttributes, test style ([750ebbb](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/750ebbbd79a988bf824bec64f58465f591bc659d))

## [0.11.3](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/compare/v0.11.2...v0.11.3) (2026-03-15)


### Bug Fixes

* chain publish job inside release-please workflow ([9f14c6a](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/9f14c6a3554190e6869a95aa7ed2e40b287ce48b))
* chain publish job inside release-please workflow ([bd3d934](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/bd3d9345575b9fa455d0689e529f6172dc9dc1e9))

## [0.11.2](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/compare/v0.11.1...v0.11.2) (2026-03-15)


### Bug Fixes

* update GitHub Packages source URL to ZeroAlloc-Net org ([a500441](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/a500441c1ac4d2a6a7d9b06e161d02bc6d4f904f))
* update GitHub Packages source URL to ZeroAlloc-Net org ([92ef6e5](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/92ef6e520511d143a87c88a999c0188eea33b05c))

## [0.11.1](https://github.com/MarcelRoozekrans/ZInject/compare/v0.11.0...v0.11.1) (2026-03-15)


### Bug Fixes

* correct remaining ZInject references in CHANGELOG, Directory.Build.props, README, and test comments ([c6d2ef1](https://github.com/MarcelRoozekrans/ZInject/commit/c6d2ef1ae86248291c4870557616a863a3196ab0))
* update MethodNamingTests to use ZeroAllocInject attribute ([aad9877](https://github.com/MarcelRoozekrans/ZInject/commit/aad987782284cff47361e5203e8e9ab6dde2a66e))
* update remaining ZInject references in benchmark descriptions and method names ([aa6028f](https://github.com/MarcelRoozekrans/ZInject/commit/aa6028f4af74b24650e94f71618ffbab3e4d0043))

## [0.11.0](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/compare/v0.10.5...v0.11.0) (2026-03-15)


### Features

* add [DecoratorOf] and [OptionalDependency] attributes ([d255080](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/d255080d8cac0faac2bbc2edb75638c7122049f8))
* add FindClosedGenericUsages analysis pass for AOT open generic discovery ([35aa149](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/35aa14950334d5d8412f84e7a1e67a735a7f0a78))
* add ZI018 warning when open generic has no detected closed usages ([6b3dda6](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/6b3dda61b9604590b36c515207adb7fb1e7b0070))
* emit explicit closed generic entries in standalone container ResolveKnown/ResolveScoped ([5570360](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/5570360e232ce5aeda7c9db7f8ff80be020e0c3a))
* emit WhenRegistered conditional guard in AddXxxServices() for [DecoratorOf] ([72a8a7c](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/72a8a7cffcd367d2c54b5028b2f42af023946372))
* extend data models with open generic metadata for AOT analysis ([a5da590](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/a5da590b5d7e75f035721bcb3f6d03646afdde6e))
* integration test and README for DecoratorOf/OptionalDependency ([811433f](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/811433f5e31b4e5beb7a729d86338bad20f064b2))
* Native AOT — compile-time closed generic enumeration ([b0d3576](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/b0d35769d48d14d3e542c0b83d148836b26fecb7))
* remove MakeGenericType open generic machinery — standalone container is now reflection-free ([c7881df](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/c7881dfba70b68c55421bfe8c9008f9387b234b0))
* sort decorators by Order and emit ZI017 for duplicate Order ([ae63a3f](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/ae63a3ffdb5eb347efb09706977470681e5b317b))


### Bug Fixes

* add IDisposable disposal-on-race for singleton closed generics, cache scoped closed generics per scope ([d389a29](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/d389a29cc5916eb3fc6b15d8948c12bc4f0694cd))
* align UnboundGenericInterfaceFqn format with Interfaces FQN format ([1f09a28](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/1f09a28b4cb39aa9950ba100686aabe2a5041374))
* apply open-generic decorator chain in standalone closed-type entries ([1e0020a](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/1e0020a07a54c11f7ae3e92ebcc9e5ec6d1d9f68))
* clarify ordering assertion comment in integration test ([6c0fd59](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/6c0fd59d0d3f3998d20345bd08990b4ce4c08437))
* dispose closed-generic singleton fields in generated container Dispose/DisposeAsync ([400e34d](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/400e34ded46e36b79d03f543c943d859268d6d37))
* emit 'using System.Linq;' in generated file when WhenRegistered guard is used ([47e05ca](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/47e05ca817fae203e53a3f0f15d57d73d71cb57e))
* emit ZI016 for [DecoratorOf] interface not implemented, not ZI011 ([91334dc](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/91334dcff30460427d9b719a19c17b28222cfa55))
* improve GetHashCode for TypeArgumentMetadataNames, fix namespace style ([10c3613](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/10c3613e2b718118b34459d7e0b2f3795e73e60a))
* sort decorator Order ascending so Order=1 is innermost (closest to implementation) ([3e054d2](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/3e054d2cdf7630f49d14a7a389e937b41d681ce7))
* trigger publish workflow on version tags only ([d7744c1](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/d7744c1e6427408f20cf81dd4608ca05ad1d9b87))
* trigger publish workflow on version tags only ([571d171](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/571d17133b2a9fd1fd0fed67088f55a8ebc2256b))

## [0.10.5](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/compare/v0.10.4...v0.10.5) (2026-03-14)


### Bug Fixes

* resolve Container analyzer DLL path reliably ([ee52f28](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/ee52f280d9fb782985a2704e1e1a49fdef9c61ee))
* use MSBuildThisFileDirectory for generator DLL path in Container package ([b5d3563](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/b5d3563a4ad81dccee746538c11ae27b0a839bf1))

## [0.10.4](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/compare/v0.10.3...v0.10.4) (2026-03-14)


### Bug Fixes

* bundle generator DLL in ZeroAlloc.Inject.Container NuGet package ([630d455](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/630d455a0fe29698021489c2e23691b75f818518))
* pack generator DLL into analyzers/dotnet/cs for NuGet packages ([83e67d1](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/83e67d195080856096f1fbaa772a79cf6f56534c))
* use forward slashes in csproj paths for cross-platform builds ([0c24beb](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/0c24bebb576f45cfc2dea07a11eef47c2214484f))

## [0.10.3](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/compare/v0.10.2...v0.10.3) (2026-03-14)


### Bug Fixes

* pack generator DLL into analyzers/dotnet/cs ([355bdc3](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/355bdc3156eb39ca2613470d2c3d8172d3b45658))
* pack generator DLL into analyzers/dotnet/cs instead of lib ([3667d9e](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/3667d9ebfa32b3bbb0a757c21dcbdf6568aff81b))

## [0.10.2](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/compare/v0.10.1...v0.10.2) (2026-03-13)


### Bug Fixes

* update GitVersion.yml to v6 format ([4f62800](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/4f628004d32aae7e8b9b047004fc2ed0c3111e19))
* update GitVersion.yml to v6 format ([e1471cb](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/e1471cb12e5b34c00cc7688dba60350b9ba08557))

## [0.10.1](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/compare/v0.10.0...v0.10.1) (2026-03-13)


### Bug Fixes

* trigger publish on tag push + add GitHub Packages ([df26ab0](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/df26ab0816e0c08539debc921463b6067e607783))
* trigger publish workflow on push to main ([4834b63](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/4834b637cd564d5ed71887ef34cd69432e14f32f))

## [0.10.0](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/compare/v0.9.0...v0.10.0) (2026-03-13)


### Features

* add benchmarks with IEnumerable and scope creation scenarios ([cd7222b](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/cd7222bd4fe50663b6884f5c64073aff9af453c9))
* add compile-time diagnostics ZI006 and ZI007 ([8370e95](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/8370e95c74b870e2c77d6cc134f73401ada5896c))
* add constructor analysis and factory lambda emission in GetServiceInfo ([184159c](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/184159cea0a0f84040fb742044da3c917c2e17fa))
* add constructor injection example to sample app ([741a5bd](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/741a5bd2fba8e8478749747cf18f479412a72b24))
* add ConstructorParameterInfo data model and extend ServiceRegistrationInfo ([1d97be4](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/1d97be40a302b1520841efc0aefc3e5a9e89d9d7))
* add core service lifetime attributes and ZeroInject assembly attribute ([2ea5acd](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/2ea5acdef993071dc9f7c220e325f00efbcd3b46))
* add DecoratorAttribute to ZeroInject package ([e126ff7](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/e126ff7317ceb7b22a2ba5abf689a254c3771bf4))
* add DecoratorRegistrationInfo, ZI011-ZI013 diagnostics, and generator pipeline wiring ([6f42647](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/6f426470578cd44f27b55350ef6d736e0d02520f))
* add generator skeleton, test infrastructure, data model, and diagnostic descriptors ([aeee61f](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/aeee61f9fcff4ff14e454c15c125a7d8952c2a82))
* add IEnumerable&lt;T&gt; support, blindspot fixes, and thread-safe scope disposal ([e6edcf7](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/e6edcf76a74e7e70c877f10bbd9d329d0f09da96))
* add net9.0 target and fix conditional MS DI references ([ee74ce8](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/ee74ce866a5b1c6eb679f93866a2cc5b7e95bc72))
* add OpenGenericEntry and runtime open-generic resolution to standalone base classes ([f28c6b2](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/f28c6b200da473ae619321ecbb189dced459216c))
* add standalone provider benchmarks for resolution and registration ([d9b6c5b](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/d9b6c5bd68a24888b2339d52286cba61bf0d91d3))
* add ZeroInject sample application ([da25917](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/da2591765fab4077225a9ac5f6e49b52c6698762))
* add ZeroInject.Container runtime base classes ([7b75997](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/7b759976884dbdf3d47d9db61ffef6da729d5a5f))
* add ZeroInjectStandaloneProvider and ZeroInjectStandaloneScope base classes ([0080be9](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/0080be9e74d1a1e6722b8d9b0b85eb341afd6b70))
* add ZI009 and ZI010 diagnostics for constructor validation ([cec517f](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/cec517fa2c7f7047b0cf1ddc702a1ed32295d130))
* code-gen open-generic delegate factories for ~6x faster standalone resolution ([5fa46fb](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/5fa46fb0ae357c4ca29ebb2c792cb16af36960a6))
* compile-time circular dependency detection (ZI014) ([39577b2](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/39577b25671fe4ead63a4399c5e9c8edc603f2cf))
* emit decorator wrapping and open-generic map in all generator outputs ([bfbeb32](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/bfbeb32e972ec62dca2683f111adb3fbd664ef72))
* emit Dispose/DisposeAsync overrides for standalone provider singletons ([9c6afec](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/9c6afec5483fe97b4b1385dab596c55d5f470abb))
* emit factory lambdas for keyed registrations ([9817583](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/98175836aed3ecf2cfadc8d7667a0d7e459c9784))
* emit IEnumerable&lt;T&gt; checks in scope ResolveScopedKnown ([1dd3ac7](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/1dd3ac7d2a03a6dd5d5e3df7855a363a1a6ce235))
* emit IsKnownService override from generator ([a96a1ff](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/a96a1ff3652536282c9d9fb2e26e952959913875))
* emit standalone provider class from generator ([6132574](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/613257452eaa3cc7f42ff8b9d1fb448703940a2f))
* generate BuildZeroInjectServiceProvider and ZeroInjectServiceProviderFactory ([7ba83a8](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/7ba83a85a15afc3b11e3fb1a67d10544d0c69b88))
* generate IServiceProvider with transient, singleton, and scoped resolution ([fedfc95](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/fedfc955ac7b6130d914581ab4ddecaf1ee2f438))
* implement core source generator with basic service discovery and registration ([70d8d1e](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/70d8d1ed94859d3199426544260731e4a73f3ef8))
* implement IServiceProviderIsKeyedService on all base classes and scopes ([aa01830](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/aa018304b662f75dcbb12d961649778452efdee0))
* implement IServiceProviderIsService on all base classes ([64b30df](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/64b30dfb81ad5b142b8c4f7b1ab75b797462560f))
* scaffold ZeroInject solution structure ([521051c](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/521051c92e6090ac0ee36bee1229aa701b6277be))
* standalone container, production features, rebrand to ZeroAlloc.Inject ([4b65c9f](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/4b65c9ff6f84069a5a80a511c247f4618bcc909b))
* support multi-decorator stacking with chained wrapping ([e9ff721](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/e9ff7211a0994e66eda55022a4d6b961434cd9a3))


### Bug Fixes

* add cancellation token check and correct open generic TryAdd semantics ([9e421e0](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/9e421e0f49007b5a1e1010d071c4bf588816b8c0))
* add missing IsKeyedService type check in generator and add generator tests ([968dc7a](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/968dc7a15040f2c6012e85280260abf7a4d4db08))
* address code review issues for keyed services and equality ([6b89c18](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/6b89c18a6dbaf73aab87da91570663f302db1ffb))
* cache keyed singletons and add null-argument validation ([15a71f3](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/15a71f32794d6380d781e6a2931f47ebe2ff4d6a))
* consolidate constructor iteration and complete equality implementations ([748d51f](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/748d51fb761761f1744338c603239a73bdca2ebc))
* GetService returns last registration (last-wins, matching MS DI) ([fdf04e5](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/fdf04e5cec5f483ac172eb1180c098d208e8ae8e))
* resolve all analyzer warnings ([d9f0532](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/d9f053202b3385d64d2c2d2c5013062c7cbc6240))
* use nullable object? for IsKnownKeyedService parameter ([2047324](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/commit/2047324806be4eadc7e61546b159eaf6ab2c3dcc))
