module Theater.ProjectionTests

open TinyEventStore.Check
open Expecto
open Expecto.Flip
open System
open Decider
open TinyEventStore.Interfaces
open TinyEventStore.EfStorage
open Microsoft.EntityFrameworkCore
open Microsoft.Extensions.DependencyInjection
open System.IO
open Store

let createServices name =
  let services = ServiceCollection()
  Directory.CreateDirectory("data") |> ignore
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
  EfStorage<TheaterStreamId, Guid, TheaterStream, TheaterState, TheaterEvent, TheaterCommand, EventDbContext>(
    db,
    efStorageOptions
  )

let printSubscription: Subscription<_, _, _, _> =
  fun ctx x ->
    task {
      printfn "subscription %A" x
      return ()
    }

let deriveListItem (eventResult: AppendEventsResult<_, TheaterState, _>) =
  {
    Id = eventResult.Id
    Name = eventResult.State.Name
  }

let defaultEfProjection (db: 'db :> DbContext) (obj: 't) (arg: AppendEventsResult<_, _, _>) =
  let set = db.Set<'t>()
  if arg.IsNew then
    set.Add obj |> ignore
  else
    set.Update obj |> ignore

let deriveListItemProjection (ctx: IServiceProvider) (arg: AppendEventsResult<_, TheaterState, _>) =
  task {
    let db = ctx.GetRequiredService<EventDbContext>()
    let obj = deriveListItem arg
    defaultEfProjection db obj arg
    return ()
  }

let ts = DateTimeOffset.Parse("2025-02-02 10:15:00")

let immediateProjectionsSubscription: OnCommittingEventHandler<_, _, _, _> =
  fun ctx x -> task { do! deriveListItemProjection ctx x }

let store (subscription) =
  EventStore(
    system,
    (fun ctx details ->
      {
        Version = ctx.Version
        TimeStamp = ts
        Data = details
      }
    ),
    [ immediateProjectionsSubscription ],
    subscription
  )

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

let tests =
  [

    ftestTask "Initial events with deletion should not create list projection" {
      let store = store ([])
      let id =
        "f1e0cdd0-b9c1-459a-b750-dfe6a42263cb" |> Guid.Parse |> TheaterStreamId.FromRaw
      let scope, storage = createContext id

      do!
        store.ApplyEvents(
          storage,
          id,
          [ TheaterEventDetails.Created "Hello World"; TheaterEventDetails.Deleted ],
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
      let store = store ([])
      let id =
        "f1e0cdd0-b9c1-459a-b750-dfe6a42263cb" |> Guid.Parse |> TheaterStreamId.FromRaw
      let scope, storage = createContext id

      do!
        store.ApplyEvents(
          storage,
          id,
          [
            TheaterEventDetails.Created "Hello World"
            TheaterEventDetails.Updated "Hello World 2"
          ],
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
      let store = store ([])
      let id =
        "f1e0cdd0-b9c1-459a-b750-dfe6a42263cb" |> Guid.Parse |> TheaterStreamId.FromRaw
      let scope, storage = createContext id

      do! store.ApplyEvents(storage, id, [ TheaterEventDetails.Created "Hello World" ], scope.ServiceProvider)

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
