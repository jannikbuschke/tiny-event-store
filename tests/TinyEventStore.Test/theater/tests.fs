module Theater.Tests

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

// type Id<'Entity> = | Id of Guid
//
// module Id =
//   let create () = Id(Guid.NewGuid())
//   let value (Id guid) = guid
//   let from (raw: Guid) = Id raw
//   let fromRaw (raw: string) = raw |> Guid.Parse |> Id


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
    Store.efStorageOptions
  )

let printSubscription: Subscription<_, _, _, _> =
  fun ctx x ->
    task {
      printfn "subscription %A" x
      return ()
    }

let ts = DateTimeOffset.Parse("2025-02-02 10:15:00")

let store (subscription) =
  EventStore(
    system,
    // storage,
    (fun ctx details ->
      {
        Version = ctx.Version
        TimeStamp = ts
        Data = details
      }
    ),
    [],
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
  storage

let tests =
  [
    testTask "subscription should be invoked" {
      let id =
        Guid.Parse "e53991ef-6969-4012-94db-7a005e962e50" |> TheaterStreamId.FromRaw
      let storage = createContext id
      let result = ResizeArray()
      let subscription: Subscription<_, _, _, _> =
        fun ctx x ->
          task {
            result.Add x
            return ()
          }
      let store = store ([ subscription ])
      let ctx = ""
      do! store.ApplyCommand(storage, id, TheaterCommand.New(Create "hello world 1", ts), ctx)
      let result = result |> Seq.toList
      expect
        <@
          result = [
            {

              Id = id
              Version = 1L
              State =
                {
                  Name = "hello world 1"
                  IsDeleted = false
                  TimeStamp = ts
                }
              Events =
                [
                  {
                    Version = 1L
                    TimeStamp = ts
                    Data = TheaterEventDetails.Created "hello world 1"
                  }
                ]
              IsNew = true
              TimeStamp = ts

            }
          ]
        @>
    }

    testTask "Applying multiple commands should create events 3" {
      let store = store ([])

      let id =
        Guid.Parse "f03606f0-a8f5-428b-8a38-6e5d77384887" |> TheaterStreamId.FromRaw

      let storage = createContext id
      let ctx = ""
      do! store.ApplyCommand(storage, id, TheaterCommand.New(Create "hello world 1", ts), ctx)
      do! store.ApplyCommand(storage, id, TheaterCommand.New(Update "hello world 2"), ctx)
      do! store.ApplyCommand(storage, id, TheaterCommand.New(Update "hello world 3"), ctx)

      let! events = storage.LoadAllEvents id
      printfn "Events\n%A" events

      expect
        <@
          events = Some
            [
              {
                Version = 1L
                TimeStamp = ts
                Data = TheaterEventDetails.Created "hello world 1"
              }
              {
                Version = 2L
                TimeStamp = ts
                Data = TheaterEventDetails.Updated "hello world 2"
              }
              {
                Version = 3L
                TimeStamp = ts
                Data = TheaterEventDetails.Updated "hello world 3"
              }
            ]
        @>
    }

    testTask "Applying multiple commands should create events 2" {
      let store = store ([])
      let id =
        Guid.Parse "7c5af6e2-01c6-474d-a95b-7aeb0dbe2bae" |> TheaterStreamId.FromRaw
      let storage = createContext id
      let ctx = ""

      printfn "send one command"
      do! store.ApplyCommand(storage, id, TheaterCommand.New(Create "hello world"), ctx)
      printfn "send another command"
      do! store.ApplyCommand(storage, id, TheaterCommand.New(Update "hello world 2"), ctx)

      let! events = storage.LoadAllEvents id
      printfn "Events\n%A" events

      expect
        <@
          events = Some
            [
              {
                Version = 1L
                TimeStamp = ts
                Data = TheaterEventDetails.Created "hello world"
              }
              {
                Version = 2L
                TimeStamp = ts
                Data = TheaterEventDetails.Updated "hello world 2"
              }
            ]
        @>
    }

    testTask "initialising command should create event" {
      let store = store ([])
      let id =
        Guid.Parse "533b2770-e6c1-40ac-b985-bc9541f937aa" |> TheaterStreamId.FromRaw
      let storage = createContext id
      let ctx = ""
      let id = Guid.NewGuid() |> TheaterStreamId.FromRaw
      do! store.ApplyCommand(storage, id, TheaterCommand.New(Create "hello world"), ctx)

      let! events = storage.LoadAllEvents id

      expect
        <@
          events = Some
            [
              {
                Version = 1L
                TimeStamp = ts
                Data = TheaterEventDetails.Created "hello world"
              }
            ]
        @>
    }

    testTask "initialising command should not error" {
      let store = store ([])
      let id =
        Guid.Parse "81ef2928-8f94-4306-b0be-9bc3b05338c8" |> TheaterStreamId.FromRaw
      let storage = createContext id
      let ctx = ""
      let! result1 = store.ApplyCommand(storage, id, TheaterCommand.New(Create "hello world"), ctx)
      result1 |> Expect.isOk "Expected ok"
    }

    testTask "non initialising command should error" {
      let store = store ([])
      let id =
        Guid.Parse "2aa36167-0a49-489c-b5df-b24ecf7ef026" |> TheaterStreamId.FromRaw
      let storage = createContext id
      let ctx = ""
      let! result1 = store.ApplyCommand(storage, id, TheaterCommand.New(Update "hello world"), ctx)
      let error =
        sprintf
          "Expected an initialising command (but got (TheaterCommand) which is not defined as an initializer), as the env stream %s does not yet have any events"
          (id.ToString())

      expect
        <@
          result1 = Error(
            {
              Message = Some error
              Details = EventStoreErrorDetails.InitializationError(InitializationError.CommandIsNotInitializer)
            }
          )
        @>
    }

    testTask "Deletion event should mark stream as deleted" {
      let store = store ([])
      let storage = createContext id
      let ctx = ""
      let id =
        Guid.Parse("b5a03c1e-daed-4b76-ad4c-5fbe9fcd3326") |> TheaterStreamId.FromRaw
      do!
        store.ApplyEvents(
          storage,
          id,
          [
            TheaterEventDetails.Created "Hello World"
            TheaterEventDetails.Updated "Hello World 2"
          ],
          ts,
          ctx
        )
      let! hydrationResult = store.Rehydrate(storage, id)
      let hydrationResult = hydrationResult |> Expect.wantSome "Expected Some"
      expect
        <@
          hydrationResult.State = {
                                    TimeStamp = ts
                                    IsDeleted = false
                                    Name = "Hello World 2"
                                  }
        @>
    }

    testTask "Appending multiple events" {
      let store = store ([])
      let storage = createContext id
      let ctx = ""
      let id =
        Guid.Parse("b5a03c1e-daed-4b76-ad4c-5fbe9fcd3326") |> TheaterStreamId.FromRaw
      do!
        store.ApplyEvents(
          storage,
          id,
          [
            TheaterEventDetails.Created "Hello World"
            TheaterEventDetails.Updated "Hello World 2"
          ],
          ts,
          ctx
        )
      let! hydrationResult = store.Rehydrate(storage, id)
      let hydrationResult = hydrationResult |> Expect.wantSome "Expected Some"
      expect
        <@
          hydrationResult.State = {
                                    TimeStamp = ts
                                    IsDeleted = false
                                    Name = "Hello World 2"
                                  }
        @>
    }

    testTask "initialising event should create stream" {
      let store = store ([])
      let storage = createContext id
      let ctx = ""
      let id =
        Guid.Parse("b5a03c1e-daed-4b76-ad4c-5fbe9fcd3326") |> TheaterStreamId.FromRaw
      do! store.ApplyEvents(storage, id, [ TheaterEventDetails.Created "Hello World" ], ts)
      let! hydrationResult = store.Rehydrate(storage, id)
      let hydrationResult = hydrationResult |> Expect.wantSome "Expected Some"
      expect <@ box hydrationResult.State <> null @>
      expect <@ hydrationResult.State <> system.aggregate.zero @>
      expect
        <@
          hydrationResult.State = {
                                    TimeStamp = ts
                                    IsDeleted = false
                                    Name = "Hello World"
                                  }
        @>
    }

    testTask "initialising events on different streams should be persisted" {
      let store = store ([])
      let storage = createContext id
      let ctx = ""

      let id = "b5a03c1e-daed-4b76-ad4c-5fbe9fcd3326" |> TheaterStreamId.FromRawString
      let evt = TheaterEventDetails.Created "Hello World 1"
      do! store.ApplyEvents(storage, id, [ evt ], ts, ctx)
      let! events = storage.LoadAllEvents id
      let events = events |> Expect.wantSome "Expected some events"
      let head = events |> Expect.wantFirst "ExpectAddEventStored at least one event"

      let id2 = "da6e9843-47ba-44d4-b572-90a6ed581add" |> TheaterStreamId.FromRawString
      let evt2 = TheaterEventDetails.Created "Hello World 2"
      do! store.ApplyEvents(storage, id2, [ evt2 ], ts, ctx)
      let! events2 = storage.LoadAllEvents id2
      let events2 = events2 |> Expect.wantSome "Expected some events"
      let head2 = events2 |> Expect.wantFirst "ExpectAddEventStored at least one event"

      expect
        <@
          head = {
                   TheaterEvent.Version = 1L
                   TimeStamp = ts
                   Data = (TheaterEventDetails.Created "Hello World 1")
                 }
        @>
      expect
        <@
          head2 = {
                    TheaterEvent.Version = 1L
                    TimeStamp = ts
                    Data = (TheaterEventDetails.Created "Hello World 2")
                  }
        @>
    }

    testTask "initialising event should be persisted" {
      let store = store ([])
      let storage = createContext id
      let ctx = ""
      let id =
        Guid.Parse("b5a03c1e-daed-4b76-ad4c-5fbe9fcd3326") |> TheaterStreamId.FromRaw
      let evt = TheaterEventDetails.Created "Hello World"
      do! store.ApplyEvents(storage, id, [ evt ], ts, ctx)
      let! events = storage.LoadAllEvents id
      let events = events |> Expect.wantSome "Expected some events"
      let head = events |> Expect.wantFirst "ExpectAddEventStoreed at least one event"
      expect
        <@
          head = {
                   TheaterEvent.Version = 1L
                   TimeStamp = ts
                   Data = evt
                 }
        @>
    }

    testTask "initialising event should not error" {
      let store = store ([])
      let storage = createContext id
      let ctx = ""
      let id =
        Guid.Parse("0041e429-c8b6-48ab-b7b7-2151940ff8bf") |> TheaterStreamId.FromRaw
      let! result1 = store.ApplyEvents(storage, id, [ TheaterEventDetails.Created "" ], ts, ctx)
      do! store.ApplyEvents(storage, id, [ TheaterEventDetails.Created "" ], ts, ctx)
      expect <@ result1 = Ok() @>
    }

    testTask "non initialising event should error" {
      let store = store ([])
      let storage = createContext id
      let ctx = ""
      let id =
        Guid.Parse("dc41f5f7-46aa-4406-99b2-61510d549b4f") |> TheaterStreamId.FromRaw
      let! result1 = store.ApplyEvents(storage, id, [ TheaterEventDetails.Deleted ], ts, ctx)
      let error = result1 |> Expect.wantError "Expected error"
      expect <@ error.Details = EventStoreErrorDetails.InitializationError(InitializationError.EventIsNotInitializer) @>
    }

    testTask "empty events should error" {
      let store = store ([])
      let storage = createContext id
      let ctx = ""
      let id =
        Guid.Parse("07b23a15-c365-4391-9e27-2413066a42c9") |> TheaterStreamId.FromRaw
      let! x = task { return 1 }

      let! result1 = store.ApplyEvents(storage, id, [], ts, ctx)
      result1 |> Expect.isError "expected ok"

      result {
        let! x = result1
        return x
      }
      |> fun x -> Expect.isOk "" |> ignore

    }

  ]
