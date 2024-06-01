namespace Extensions

open System.Runtime.CompilerServices
open System
open Microsoft.Extensions.DependencyInjection

type ServiceCollectionExtensions =
  [<Extension>]
  static member inline AddDefaultTimeProvider(services: IServiceCollection) =
    services.AddSingleton(TimeProvider.System)

  [<Extension>]
  static member inline GetTimeProviderOrDefault(services: IServiceProvider) =
    let timeProvider = services.GetService<TimeProvider>()

    if timeProvider = null then
      TimeProvider.System
    else
      timeProvider
