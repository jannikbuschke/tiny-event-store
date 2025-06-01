module Theater2.Tests2

open TinyEventStore.Check
open Expecto
open Expecto.Flip
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
  db.Database.EnsureDeleted() |> ignore
  db.Database.EnsureCreated() |> ignore
  provider

let getStorage (services: IServiceProvider) =
  let db = services.GetService<EventDbContext>()
  EfSimpleStorage<TheaterState, TheaterEvent, TheaterCommand, EventDbContext>(db, Store.efStorageOptions)

let printSubscription: Subscription<_, _, _> =
  fun _ x ->
    task {
      printfn "subscription %A" x
      return ()
    }

let ts = DateTimeOffset.Parse "2025-02-02 10:15:00"

let store subscriptions =
  EventStore<TheaterState, TheaterEvent, TheaterCommand, IServiceProvider>(system, [], subscriptions)

module Expect =
  let wantFirst msg l =
    match l with
    | [] -> failtest msg
    | head :: _ -> head

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

let createContext0 (services: IServiceProvider) =
  let scope = services.CreateAsyncScope()
  let storage = getStorage scope.ServiceProvider
  storage, services

let createContext id =
  let services = createServices (id.ToString())
  createContext0 services

let extractVersionAndDetails (events: NonEmptyList<TheaterEvent> option) =
  let events = events |> Expect.wantSome "Expected Some"
  events.List
  |> List.map (fun x ->
    {|
      Version = x.Version
      Details = x.Details
    |}
  )

let tests =
  [

    test "test" {
      let ts1 = DateTimeOffset.Parse "2025-02-02 10:15:00"
      let ts2 = DateTimeOffset.Parse "2025-02-02 10:15:00"
      let ts2 = ts2.AddSeconds 0
      expect <@ ts1 = ts2 @>
    }

    ftestTask "subscription should be invoked" {
      let id = Guid.Parse "e53991ef-6969-4012-94db-7a005e962e50"
      let storage, ctx = createContext id
      let result = ResizeArray()
      let subscription: Subscription<_, _, _> =
        fun _ x ->
          task {
            result.Add x
            return ()
          }
      let store = store [ subscription ]
      do! store.ApplyCommand(storage, (id, ts), TheaterCommand.New(Create "hello world 1", ts), ctx)
      let eid = result.Item(0).Events.Head.Id
      let result = result |> Seq.toList
      expect
        <@
          result = [
            {
              Id = id
              Version = 1L
              State =
                {
                  Created = ts
                  Name = "hello world 1"
                  IsDeleted = false
                  Modified = ts
                }
              Events =
                NonEmptyList.UnsafeFrom
                  [
                    {
                      TheaterEvent.StreamId = id
                      TheaterEvent.Version = 1L
                      TheaterEvent.TimeStamp = ts
                      TheaterEvent.Details = TheaterEventDetails.Created "hello world 1"
                      TheaterEvent.Id = eid
                    }
                  ]
              IsNew = true
              TimeStamp = ts
            }
          ]
        @>
    }

    testTask "Appending two events on different scopes" {
      let store = store []
      let id = Guid.Parse "95fdaac8-364f-4923-8703-b050187eac17"
      use services = createServices id

      let storage, ctx = createContext0 services
      let t0 = DateTimeOffset.FromUnixTimeSeconds 1000000
      let events = [ TheaterEventDetails.Created "hello world 1" ] |> createEvents
      do! store.ApplyEvents(storage, id, events, t0, ctx)
      let storage, ctx = createContext0 services
      let! stream0 = (storage :> ISimpleEventStorage<_, _, _>).LoadStream id
      let stream0: IStreamDbo = stream0 |> Expect.wantOk "Expected ok"

      let modifiedTimestamp = stream0.Modified
      let createdTimestamp = stream0.Created
      expect <@ createdTimestamp = t0 @>
      expect <@ modifiedTimestamp = t0 @>

      let t1 = DateTimeOffset.FromUnixTimeSeconds 1100000
      do! store.ApplyEvents(storage, id, [ TheaterEventDetails.Updated "hello world 2" ] |> createEvents, t1, ctx)
      let storage, ctx = createContext0 services

      let! stream = (storage :> ISimpleEventStorage<_, _, _>).LoadStream id

      let stream: IStreamDbo = stream |> Expect.wantOk "Expected ok"

      let modifiedTimestamp = stream.Modified
      let createdTimestamp = stream.Created
      expect <@ createdTimestamp = t0 @>
      expect <@ modifiedTimestamp = t1 @>

    }

    testTask "Applying multiple commands should update stream" {
      let store = store []
      let id = Guid.Parse "45465935-b61e-478a-bf89-2c86816e5588"
      let storage, ctx = createContext id
      do! store.ApplyCommand(storage, (id, ts.AddSeconds 0), TheaterCommand.New(Create "hello world 1", ts), ctx)
      do! store.ApplyCommand(storage, (id, ts.AddSeconds 1), TheaterCommand.New(Update "hello world 2"), ctx)
      do! store.ApplyCommand(storage, (id, ts.AddSeconds 2), TheaterCommand.New(Update "hello world 3"), ctx)
      let! stream = (storage :> ISimpleEventStorage<_, _, _>).LoadStream id
      let stream: IStreamDbo = stream |> Expect.wantOk "Expected Ok"


      // expect <@ stream.Created = ts @>
      // expect <@ stream.Modified = ts.AddSeconds 2 @>
      // todo fix
      expect <@ stream.Version = 3L @>

    }

    testTask "Applying multiple commands should create events 3" {
      let store = store []
      let id = Guid.Parse "f03606f0-a8f5-428b-8a38-6e5d77384887"
      let storage, ctx = createContext id
      do! store.ApplyCommand(storage, (id, ts.AddSeconds 0), TheaterCommand.New(Create "hello world 1", ts), ctx)
      do! store.ApplyCommand(storage, (id, ts.AddSeconds 1), TheaterCommand.New(Update "hello world 2"), ctx)
      do! store.ApplyCommand(storage, (id, ts.AddSeconds 2), TheaterCommand.New(Update "hello world 3"), ctx)
      let! events = storage.LoadAllEvents id
      let events = events |> extractVersionAndDetails
      expect
        <@
          events = [
            {|
              Version = 1L
              Details = TheaterEventDetails.Created "hello world 1"
            |}
            {|
              Version = 2L
              Details = TheaterEventDetails.Updated "hello world 2"
            |}
            {|
              Version = 3L
              Details = TheaterEventDetails.Updated "hello world 3"
            |}
          ]
        @>
    }

    testTask "Applying multiple commands should create events 2" {
      let store = store []
      let id = Guid.Parse "7c5af6e2-01c6-474d-a95b-7aeb0dbe2bae"
      let storage, ctx = createContext id
      do! store.ApplyCommand(storage, (id, ts), TheaterCommand.New(Create "hello world"), ctx)
      do! store.ApplyCommand(storage, (id, ts.AddSeconds 1), TheaterCommand.New(Update "hello world 2"), ctx)
      let! events = storage.LoadAllEvents id
      let events = events |> extractVersionAndDetails
      expect
        <@
          events = [
            {|
              Version = 1L
              Details = TheaterEventDetails.Created "hello world"
            |}
            {|
              Version = 2L
              Details = TheaterEventDetails.Updated "hello world 2"
            |}
          ]
        @>
    }

    testTask "initialising command should initialise stream" {
      let store = store []
      let id = Guid.Parse "5a6b3810-7c85-4061-832b-77db8f539d0a"
      let storage, ctx = createContext id
      let id = Guid.NewGuid()
      let cmd = TheaterCommand.New(Create "hello world")
      do! store.ApplyCommand(storage, (id, ts), cmd, ctx)
      let! (stream: Dtos.StreamDto<TheaterEvent> option) = storage.GetStream id
      let stream = stream |> Expect.wantSome "Expected Some"
      expect <@ stream.Id = id @>
      expect <@ stream.Created = ts @>
      expect <@ stream.IsDeleted = false @>
    }

    testTask "initialising command should create event" {
      let store = store []
      let id = Guid.Parse "533b2770-e6c1-40ac-b985-bc9541f937aa"
      let storage, ctx = createContext id
      let id = Guid.NewGuid()
      let cmd = TheaterCommand.New(Create "hello world")
      do! store.ApplyCommand(storage, (id, ts), cmd, ctx)
      let! events = storage.LoadAllEvents id
      let events: NonEmptyList<TheaterEvent> = events |> Expect.wantSome "Expected Some"
      let head = events.Head
      expect <@ head.Version = 1L @>
      expect <@ head.Details = TheaterEventDetails.Created "hello world" @>
    }

    testTask "initialising command should not error" {
      let store = store []
      let id = Guid.Parse "81ef2928-8f94-4306-b0be-9bc3b05338c8"
      let storage, ctx = createContext id
      let! result1 = store.ApplyCommand(storage, (id, ts), TheaterCommand.New(Create "hello world"), ctx)
      result1 |> Expect.isOk "Expected ok"
    }

    testTask "non initialising command should error" {
      let store = store []
      let id = Guid.Parse "2aa36167-0a49-489c-b5df-b24ecf7ef026"
      let storage, ctx = createContext id
      let! result1 = store.ApplyCommand(storage, (id, ts), TheaterCommand.New(Update "hello world"), ctx)
      let error =
        sprintf
          "Expected an initialising command (but got (TheaterCommand) which is not defined as an initializer), as the env stream %s does not yet have any events"
          (id.ToString())
      expect
        <@
          result1 = Error(
            {
              // Message = Some error
              Message = None
              Details = EventStoreErrorDetails.InitializationError InitializationError.CommandIsNotInitializer
            }
          )
        @>
    }

    testTask "Deletion event should mark state as deleted" {
      let store = store []
      let storage, ctx = createContext id
      let id = Guid.Parse "b5a03c1e-daed-4b76-ad4c-5fbe9fcd3326"
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
          ctx
        )
      let! hydrationResult = store.Rehydrate(storage, id)
      let hydrationResult: HydrationResult<_, _> = hydrationResult
      // let hydrationResult = hydrationResult |> Expect.wantSome "Expected Some"
      let value = hydrationResult.ValueOrError() |> Expect.wantOk "Expected Ok"
      expect
        <@
          value.State = {
                          Modified = ts
                          Created = ts
                          IsDeleted = false
                          Name = "Hello World 2"
                        }
        @>
    }

    testTask "Appending multiple events" {
      let store = store []
      let storage, ctx = createContext id
      let id = Guid.Parse "b5a03c1e-daed-4b76-ad4c-5fbe9fcd3326"
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
          ctx
        )
      let! hydrationResult = store.Rehydrate(storage, id)
      let hydrationResult: HydrationResult<_, _> = hydrationResult

      let x = hydrationResult.ValueOrError()
      let value = x |> Expect.wantOk "expected Ok"
      expect <@ value.Version = 2 @>
      expect
        <@
          value.State = {
                          Modified = ts
                          Created = ts
                          IsDeleted = false
                          Name = "Hello World 2"
                        }
        @>

      ()

    }

    testTask "initialising event should initialise streamdbo" {
      let store = store []
      let storage, ctx = createContext id
      let id = Guid.Parse "1f6738bc-796a-4cb8-901d-a312e3d650d1"
      do! store.ApplyEvents(storage, id, createEvents [ TheaterEventDetails.Created "Hello World" ], ts, ctx)
      let! (stream: Dtos.StreamDto<TheaterEvent> option) = storage.GetStream id
      let stream = stream |> Expect.wantSome "Expected Some"
      expect <@ stream.Id = id @>
    }

    testTask "initialising event should create stream" {
      let store = store []
      let storage, ctx = createContext id
      let id = Guid.Parse "b5a03c1e-daed-4b76-ad4c-5fbe9fcd3326"
      do! store.ApplyEvents(storage, id, createEvents [ TheaterEventDetails.Created "Hello World" ], ts, ctx)
      let! hydrationResult = store.Rehydrate(storage, id)
      match hydrationResult with
      | HydrationResult.NotStarted -> failtest "expected a stream"
      | HydrationResult.Value v ->
        expect <@ v.Version = 1L @>
        expect
          <@
            v.State = {
                        Modified = ts
                        Created = ts
                        IsDeleted = false
                        Name = "Hello World"
                      }
          @>

    // expect <@ box hydrationResult.State <> null @>
    // expect <@ hydrationResult.State <> system.aggregate.zero @>
    // expect
    //   <@
    //     hydrationResult.State =
    //   @>
    //   c

    }

    testTask "initialising events on different streams should be persisted" {
      let store = store []
      let storage, ctx = createContext id
      let id = Guid.Parse "b5a03c1e-daed-4b76-ad4c-5fbe9fcd3326"
      let evt = TheaterEventDetails.Created "Hello World 1"
      do! store.ApplyEvents(storage, id, createEvents [ evt ], ts, ctx)
      let! events = storage.LoadAllEvents id
      let events = events |> Expect.wantSome "Expected some events"
      let head: TheaterEvent = events.Head
      expect <@ head.Version = 1L @>
      expect <@ head.Details = evt @>
      // let v1 = head.Version
      // let head = events |> Expect.wantFirst "ExpectAddEventStored at least one event"
      let id2 = "da6e9843-47ba-44d4-b572-90a6ed581add" |> Guid.Parse
      let evt2 = TheaterEventDetails.Created "Hello World 2"
      do! store.ApplyEvents(storage, id2, createEvents [ evt2 ], ts, ctx)
      let! events2 = storage.LoadAllEvents id2
      let events2 = events2 |> Expect.wantSome "Expected some events"
      let head2: TheaterEvent = events2.Head

      expect <@ head2.Version = 1L @>
      expect <@ head2.Details = evt2 @>

    }

    testTask "initialising event should be persisted" {
      let store = store []
      let storage, ctx = createContext id
      let id = Guid.Parse "b5a03c1e-daed-4b76-ad4c-5fbe9fcd3326"
      let evt = TheaterEventDetails.Created "Hello World"
      do! store.ApplyEvents(storage, id, createEvents [ evt ], ts, ctx)
      let! events = storage.LoadAllEvents id
      let events: NonEmptyList<TheaterEvent> =
        events |> Expect.wantSome "Expected some events"
      let head =
        events.List |> Expect.wantFirst "ExpectAddEventStoreed at least one event"
      // |> versionAndDetails

      expect <@ head.StreamId = id @>
      let details = head.Details
      expect <@ details = evt @>

    }

    testTask "initialising event should not error" {
      let store = store []
      let storage, ctx = createContext id
      let id = Guid.Parse "0041e429-c8b6-48ab-b7b7-2151940ff8bf"
      let! result1 = store.ApplyEvents(storage, id, createEvents [ TheaterEventDetails.Created "" ], ts, ctx)
      result1 |> Expect.isOk "expected ok 1"
      let! result2 = store.ApplyEvents(storage, id, createEvents [ TheaterEventDetails.Created "" ], ts, ctx)
      result2 |> Expect.isOk "expected ok 2"
    }

    testTask "non initialising event should error" {
      let store = store []
      let storage, ctx = createContext id
      let id = Guid.Parse "dc41f5f7-46aa-4406-99b2-61510d549b4f"
      let! result1 = store.ApplyEvents(storage, id, createEvents [ TheaterEventDetails.Deleted ], ts, ctx)
      let error = result1 |> Expect.wantError "Expected error"
      expect <@ error.Details = EventStoreErrorDetails.InitializationError InitializationError.EventIsNotInitializer @>
    }


  ]
