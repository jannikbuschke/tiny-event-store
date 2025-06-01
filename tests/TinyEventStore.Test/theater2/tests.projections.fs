module Theater2.ProjectionTests

open TinyEventStore.Check
open Expecto
open System
open Decider
open TinyEventStore.InterfacesSimple
open TinyEventStore.EfSimpleStorage
open Microsoft.EntityFrameworkCore
open Microsoft.Extensions.DependencyInjection
open System.IO
open Store
open TinyEventStore.Simple

let createServices name =
  let services = ServiceCollection()
  Directory.CreateDirectory "data" |> ignore
  services.AddDbContext<EventDbContext>(fun options ->
    options.UseSqlite($"Data Source=data/test.{name}.sqlite").EnableSensitiveDataLogging().EnableDetailedErrors()
    |> ignore
  )
  |> ignore

  let provider = services.BuildServiceProvider()
  use scope = provider.CreateScope()
  use db = scope.ServiceProvider.GetService<EventDbContext>()
  // let db = new EventDbContext(options.Options)
  db.Database.EnsureDeleted() |> ignore
  db.Database.EnsureCreated() |> ignore
  provider

let getStorage (services: IServiceProvider) =
  let db = services.GetService<EventDbContext>()
  EfSimpleStorage<TheaterState, TheaterEvent, TheaterCommand, EventDbContext>(db, efStorageOptions)

let printSubscription: Subscription<_, _, _> =
  fun _ x ->
    task {
      printfn "subscription %A" x
      return ()
    }

let deriveListItem (eventResult: AppendEventsResult<TheaterState, _>) =
  {
    Id = eventResult.Id
    Name = eventResult.State.Name
  }

// let deriveListItemProjection (ctx: IServiceProvider) (arg: AppendEventsResult<_, TheaterState, TheaterEvent>) =
//   task {
//     let db = ctx.GetRequiredService<EventDbContext>()
//     let obj = deriveListItem arg
//     let shouldDelete = arg.Events |> List.exists (fun x -> x.Data.IsDeleted)
//     Projection.defaultEfProjection db obj arg shouldDelete
//     return ()
//   }

let listItemProjection: Projection.DeriveProjection<_, TheaterEvent, _, _> =
  {
    Derive = deriveListItem
    ShouldDelete = fun x -> x.Events.List |> List.exists _.Details.IsDeleted
  }

let ts = DateTimeOffset.Parse "2025-02-02 10:15:00"

let immediateProjectionsSubscription: OnCommittingEventHandler<_, _, _> =
  fun (ctx: IServiceProvider) x ->
    task {
      let db = ctx.GetRequiredService<EventDbContext>()
      do! Projection.handler db x listItemProjection
    }

let store subscription =
  EventStore(system, [ immediateProjectionsSubscription ], subscription)

open FsToolkit.ErrorHandling

module Expect =
  let wantFirst msg l =
    match l with
    | [] -> failtest msg
    | head :: _ -> head

let createContext id =
  let services = createServices (id.ToString())
  let scope = services.CreateAsyncScope()
  let storage = getStorage scope.ServiceProvider
  scope, storage

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

    testTask "Event after delete should not have an effect" {
      let store = store []
      let id = "cca1604d-81c6-4612-8008-ab28e4d92d0b" |> Guid.Parse
      let scope, storage = createContext id
      do!
        store.ApplyEvents(
          storage,
          id,
          createEvents [ TheaterEventDetails.Created "Hello World"; TheaterEventDetails.Deleted ],
          ts,
          scope.ServiceProvider
        )
      let db = storage.Db
      let! listItems = db.ListItems().CountAsync()
      expect <@ listItems = 0 @>
      let scope, storage = createContext id
      do!
        store.ApplyEvents(
          storage,
          id,
          createEvents [ TheaterEventDetails.Updated "Hello World 2" ],
          ts,
          scope.ServiceProvider
        )
      let! listItems = db.ListItems().CountAsync()
      expect <@ listItems = 0 @>
    }

    testTask "Deleted should delete projection" {
      let store = store []
      let id = "a521feb3-ff9a-4c20-b72d-74e874f262ff" |> Guid.Parse
      let scope, storage = createContext id
      do!
        store.ApplyEvents(
          storage,
          id,
          createEvents [ TheaterEventDetails.Created "Hello World" ],
          ts,
          scope.ServiceProvider
        )
      let db = storage.Db
      let! listItems = db.ListItems().CountAsync()
      expect <@ listItems = 1 @>
      let scope, storage = createContext id
      do! store.ApplyEvents(storage, id, createEvents [ TheaterEventDetails.Deleted ], ts, scope.ServiceProvider)
      let! listItems = db.ListItems().CountAsync()
      expect <@ listItems = 0 @>
    }

    testTask "Initial events with deletion should not create list projection" {
      let store = store []
      let id = "5c94b2b8-4569-430f-b9e2-576aa2c4cbba" |> Guid.Parse
      let scope, storage = createContext id

      do!
        store.ApplyEvents(
          storage,
          id,
          createEvents [ TheaterEventDetails.Created "Hello World"; TheaterEventDetails.Deleted ],
          ts,
          scope.ServiceProvider
        )

      let db = storage.Db
      let! listItems = db.ListItems().ToListAsync() |> Task.map Seq.toList
      expect
        <@
          listItems = [

          ]
        @>

    }

    testTask "Initial events should create list projection" {
      let store = store []
      let id = "bb994f4b-5026-4234-8b48-9ee53d94d239" |> Guid.Parse
      let scope, storage = createContext id

      do!
        store.ApplyEvents(
          storage,
          id,
          createEvents
            [
              TheaterEventDetails.Created "Hello World"
              TheaterEventDetails.Updated "Hello World 2"
            ],
          ts,
          scope.ServiceProvider
        )

      let db = storage.Db
      let! listItems = db.ListItems().ToListAsync() |> Task.map Seq.toList
      expect
        <@
          listItems = [
            {
              Id = id
              Name = "Hello World 2"
            }
          ]
        @>

    }

    testTask "Initial event should create list projection" {
      let store = store []
      let id = "f1e0cdd0-b9c1-459a-b750-dfe6a42263cb" |> Guid.Parse
      let scope, storage = createContext id

      do!
        store.ApplyEvents(
          storage,
          id,
          createEvents [ TheaterEventDetails.Created "Hello World" ],
          ts,
          scope.ServiceProvider
        )

      let db = storage.Db
      let! listItems = db.ListItems().ToListAsync() |> Task.map Seq.toList
      expect
        <@
          listItems = [
            {
              Id = id
              Name = "Hello World"
            }
          ]
        @>

    }


  ]
