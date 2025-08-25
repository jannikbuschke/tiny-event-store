module Theater2.BackgroundProjectionTests

open TinyEventStore.Check
open Expecto
open System
open Decider
open TinyEventStore.InterfacesSimple
// open TinyEventStore.EfSimpleStorage
open Microsoft.EntityFrameworkCore
open Microsoft.Extensions.DependencyInjection
open System.IO
open Store
open TinyEventStore.Simple
open FsToolkit.ErrorHandling
open System.Threading.Channels
open TinyEventStore.EfSimpleStorage.Projection
open System.Threading
open TinyEventStore.Ef.Core

let channel, subscription = TinyEventStore.Subscriptions.createChannelSubscription<TheaterEvent,_,_>()

let store () =
  EventStore(system, [], [subscription])

let createServices name =
   Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
    .ConfigureServices(fun (ctx) (services: IServiceCollection) ->
        Directory.CreateDirectory "data" |> ignore
        services.AddDbContext<EventDbContext>(fun options ->
          options
            .UseSqlite($"Data Source=data/test.{name}.sqlite")
            // .EnableSensitiveDataLogging()
            // .EnableDetailedErrors()
          |> ignore
        )
        |> ignore
        services.AddScoped<ISimpleEventStorage<TheaterEvent>>(fun p ->
          let db = p.GetService<EventDbContext>()
          db.TheaterEventStorage ()
        ) |> ignore
        // let src = TokenCancellationSource()
        services.AddHostedService<ProjectionsHostService<TheaterEvent>>(fun provider ->
          let projectionsHostService = new ProjectionsHostService<TheaterEvent>(provider, channel.Reader)
          projectionsHostService.StartAsync(CancellationToken()) |> ignore
          projectionsHostService
        ) |> ignore
        services.AddScoped<IProjectionService<TheaterEvent>>(fun provider ->
          EfProjectionService(provider, backgroundProjection)
        ) |> ignore
        let provider = services.BuildServiceProvider()
        use scope = provider.CreateScope()
        use db = scope.ServiceProvider.GetService<EventDbContext>()
        db.Database.EnsureDeleted() |> ignore
        db.Database.EnsureCreated() |> ignore
        ()
    ).Build()

let ts = DateTimeOffset.Parse "2025-02-02 10:15:00"

let createContext (services: IServiceProvider) =
  let scope = services.CreateAsyncScope()
  let db = scope.ServiceProvider.GetService<EventDbContext>()
  scope, db, db.TheaterEventStorage()

let createEvents details (ctx: CreateEventsContext) =
  details
  |> List.mapi (fun i detail ->
    {
      TheaterEvent.StreamId = ctx.StreamId
      Id = Guid.CreateVersion7(ctx.TimeStamp.AddMicroseconds i)
      Details = detail
      Version = V.incrementBy (ctx.Version, i + 1)
      TimeStamp = ctx.TimeStamp
    }
  )
  |> NonEmptyList.UnsafeFrom

let tests =
  [

    ftestTask "restart should restore deleted data" {
      let store = store ()
      let host = createServices()
      let ct = CancellationToken()
      let! _ = host.StartAsync(ct)
      let id = "bf0a4037-4591-46ef-a693-d5bb8ab58b25" |> Guid.Parse
      let scope, _, storage = createContext host.Services
      do!
        store.ApplyEvents(
          storage,
          id,
          createEvents [ TheaterEventDetails.Created "Hello World" ],
          ts,
          scope.ServiceProvider
        )
      do! Threading.Tasks.Task.Delay 100
      let db = storage.Db
      let! listItems = db.BackgroundListItems().CountAsync()
      expect <@ listItems = 1 @>

      db.BackgroundListItems().ExecuteDelete ()|>ignore
      let! listItems = db.BackgroundListItems().CountAsync()
      expect <@ listItems = 0 @>

      let scope = host.Services.CreateScope ()
      let svc = scope.ServiceProvider.GetService<IProjectionService<TheaterEvent>>()

      let ct = CancellationToken()
      do! svc.Restart ct

      let! listItems = db.BackgroundListItems().CountAsync()
      expect <@ listItems = 1 @>
      let! _ = host.StopAsync()
      ()

    }

    testTask "background projection should create one item on init event" {
      let store = store ()
      let host = createServices()
      let ct = CancellationToken()
      let! _ = host.StartAsync(ct)
      let scope = host.Services.CreateScope ()
      let id = "bf0a4037-4591-46ef-a693-d5bb8ab58b25" |> Guid.Parse
      let scope, db, storage = createContext host.Services
      do!
        store.ApplyEvents(
          storage,
          id,
          createEvents [ TheaterEventDetails.Created "Hello World" ],
          ts,
          scope.ServiceProvider
        )
      do! Threading.Tasks.Task.Delay 100
      let db = storage.Db
      let! listItems = db.BackgroundListItems().CountAsync()
      let! _ = host.StopAsync()
      expect <@ listItems = 1 @>

      // let scope, db, storage = createContext id
      // // act
      // do!
      //   store.ApplyEvents(
      //     storage,
      //     id,
      //     createEvents [ TheaterEventDetails.Updated "Hello World 2" ],
      //     ts,
      //     scope.ServiceProvider
      //   )
      // // assert
      // let! listItems = db.ListItems().CountAsync()
      // expect <@ listItems = 0 @>

    }

    // testTask "create and delete should result in empty projection list" {
    //   let store = store()
    //
    //   let id = "cca1604d-81c6-4612-8008-ab28e4d92d0b" |> Guid.Parse
    //   let scope, db, storage = createContext id
    //   do!
    //     store.ApplyEvents(
    //       storage,
    //       id,
    //       createEvents [ TheaterEventDetails.Created "Hello World"; TheaterEventDetails.Deleted ],
    //       ts,
    //       scope.ServiceProvider
    //     )
    //   let db = storage.Db
    //   let! listItems = db.ListItems().CountAsync()
    //   expect <@ listItems = 0 @>
    //   let scope, db, storage = createContext id
    //   do!
    //     store.ApplyEvents(
    //       storage,
    //       id,
    //       createEvents [ TheaterEventDetails.Updated "Hello World 2" ],
    //       ts,
    //       scope.ServiceProvider
    //     )
    //   let! listItems = db.ListItems().CountAsync()
    //   expect <@ listItems = 0 @>
    // }
    //
    // testTask "Event after delete should not have an effect" {
    //   let store = store()
    //   let id = "cca2704d-81c6-4612-8008-ab28e4d92d0b" |> Guid.Parse
    //   let scope, db, storage = createContext id
    //   do!
    //     store.ApplyEvents(
    //       storage,
    //       id,
    //       createEvents [ TheaterEventDetails.Created "Hello World"; TheaterEventDetails.Deleted ],
    //       ts,
    //       scope.ServiceProvider
    //     )
    //   let db = storage.Db
    //   let! listItems = db.ListItems().CountAsync()
    //   expect <@ listItems = 0 @>
    //   let scope, db, storage = createContext id
    //   let storage = db.TheaterEventStorage()
    //   do!
    //     store.ApplyEvents(
    //       storage,
    //       id,
    //       createEvents [ TheaterEventDetails.Updated "Hello World 2" ],
    //       ts,
    //       scope.ServiceProvider
    //     )
    //   let! listItems = db.ListItems().CountAsync()
    //   expect <@ listItems = 0 @>
    // }
    //
    // testTask "Deleted should delete projection" {
    //   let store = store()
    //   let id = "a521feb3-ff9a-4c20-b72d-74e874f262ff" |> Guid.Parse
    //   let scope, db, storage = createContext id
    //   let storage = db.TheaterEventStorage()
    //   do!
    //     store.ApplyEvents(
    //       storage,
    //       id,
    //       createEvents [ TheaterEventDetails.Created "Hello World" ],
    //       ts,
    //       scope.ServiceProvider
    //     )
    //   let db = storage.Db
    //   let! listItems = db.ListItems().CountAsync()
    //   expect <@ listItems = 1 @>
    //   let scope, db, storage = createContext id
    //   do! store.ApplyEvents(storage, id, createEvents [ TheaterEventDetails.Deleted ], ts, scope.ServiceProvider)
    //   let! listItems = db.ListItems().CountAsync()
    //   expect <@ listItems = 0 @>
    // }
    //
    // testTask "Initial events with deletion should not create list projection" {
    //   let store = store()
    //   let id = "5c94b2b8-4569-430f-b9e2-576aa2c4cbba" |> Guid.Parse
    //   let scope, db, storage = createContext id
    //
    //   do!
    //     store.ApplyEvents(
    //       storage,
    //       id,
    //       createEvents [ TheaterEventDetails.Created "Hello World"; TheaterEventDetails.Deleted ],
    //       ts,
    //       scope.ServiceProvider
    //     )
    //
    //   let db = storage.Db
    //   let! listItems = db.ListItems().ToListAsync() |> Task.map Seq.toList
    //   expect <@ listItems = [] @>
    //
    // }
    //
    // testTask "Initial events should create list projection" {
    //   let store = store()
    //   let id = "bb994f4b-5026-4234-8b48-9ee53d94d239" |> Guid.Parse
    //   let scope, db, storage = createContext id
    //
    //   do!
    //     store.ApplyEvents(
    //       storage,
    //       id,
    //       createEvents
    //         [
    //           TheaterEventDetails.Created "Hello World"
    //           TheaterEventDetails.Updated "Hello World 2"
    //         ],
    //       ts,
    //       scope.ServiceProvider
    //     )
    //
    //   let db = storage.Db
    //   let! listItems = db.ListItems().ToListAsync() |> Task.map Seq.toList
    //   expect
    //     <@
    //       listItems = [
    //         {
    //           Id = id
    //           Name = "Hello World 2"
    //         }
    //       ]
    //     @>
    //
    // }

    // testTask "Initial event should create list projection" {
    //   let store = store()
    //   let id = "f1e0cdd0-b9c1-459a-b750-dfe6a42263cb" |> Guid.Parse
    //   let scope, db, storage = createContext id
    //
    //   do!
    //     store.ApplyEvents(
    //       storage,
    //       id,
    //       createEvents [ TheaterEventDetails.Created "Hello World" ],
    //       ts,
    //       scope.ServiceProvider
    //     )
    //
    //   let db = storage.Db
    //   let! listItems = db.ListItems().ToListAsync() |> Task.map Seq.toList
    //   expect
    //     <@
    //       listItems = [
    //         {
    //           Id = id
    //           Name = "Hello World"
    //         }
    //       ]
    //     @>
    //
    // }


  ]
