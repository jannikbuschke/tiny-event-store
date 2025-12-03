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

let versionAndDetails (x: TheaterEventEnvelope) =
  {|
    Version = x.Version
    Data = x.Details
  |}

let extractVersionAndDetails (e: TheaterEventEnvelope list option) =
  e |> Expect.wantSome "Expected Some" |> List.map versionAndDetails

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
  use db = scope.ServiceProvider.GetRequiredService<EventDbContext>()
  // let db = new EventDbContext(options.Options)
  db.Database.EnsureDeleted() |> ignore
  db.Database.EnsureCreated() |> ignore
  provider

let getStorage (services: IServiceProvider) =
  let db = services.GetRequiredService<EventDbContext>()
  EfStorage<TheaterStreamId, Guid, TheaterState, TheaterEventDetails, TheaterCommand, EventDbContext>(
    db,
    Store.efStorageOptions
  )

let printSubscription: Subscription<_, _, _, _> =
  fun _ x ->
    task {
      printfn "subscription %A" x
      return ()
    }

let ts = DateTimeOffset.Parse "2025-02-02 10:15:00"

let store _ =
  EventStore<
    TheaterStreamId,
    _,
    TheaterState,
    TheaterEventEnvelope,
    TheaterCommand,
    TheaterEventDetails,
    IServiceProvider
   >(
    system,
    // evtl ueberfluessig?
    (fun ctx details ->
      {
        Version = ctx.Version
        Details = details
        TimeStamp = ts
        // Data = details
        EventId = ctx.EventId
        StreamId = ctx.StreamId
        Causation = ctx.Causation
      // CausationId = ctx.CausationId
      }
    ),
    [],
    []
  )

open FsToolkit.ErrorHandling

module Expect =
  let wantFirst msg l =
    match l with
    | [] -> failtest msg
    | head :: _ -> head

let createContext0 (services: IServiceProvider) =
  let scope = services.CreateAsyncScope()
  let storage = getStorage scope.ServiceProvider
  storage, services

let createContext id =
  let services = createServices (id.ToString())
  createContext0 services
// let scope = services.CreateAsyncScope()
// let storage = getStorage scope.ServiceProvider
// storage, services

let tests =
  [
    testTask "subscription should be invoked" {
      let id =
        Guid.Parse "e53991ef-6969-4012-94db-7a005e962e50" |> TheaterStreamId.FromRaw
      let storage, ctx = createContext id
      let result = ResizeArray()
      let subscription: Subscription<_, _, _, _> =
        fun _ x ->
          task {
            result.Add x
            return ()
          }
      let store = store [ subscription ]
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
                    TheaterEventEnvelope.StreamId = id
                    TheaterEventEnvelope.Version = 1L
                    TheaterEventEnvelope.TimeStamp = ts
                    TheaterEventEnvelope.Details = TheaterEventDetails.Created "hello world 1"
                    TheaterEventEnvelope.EventId = Guid.Empty |> TinyEventStore.EventId.FromRawValue
                    TheaterEventEnvelope.Causation = None
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
      let id =
        Guid.Parse "95fdaac8-364f-4923-8703-b050187eac17" |> TheaterStreamId.FromRaw
      use services = createServices id

      let storage, ctx = createContext0 services
      let t0 = DateTimeOffset.FromUnixTimeSeconds 1000000
      do! store.ApplyEvents(storage, id, [ TheaterEventDetails.Created "hello world 1" ], t0, ctx)
      let storage, ctx = createContext0 services

      let t1 = DateTimeOffset.FromUnixTimeSeconds 1100000
      do! store.ApplyEvents(storage, id, [ TheaterEventDetails.Updated "hello world 2" ], t1, ctx)
      let storage, ctx = createContext0 services

      let! stream = (storage :> IEventStorage<_, _, _, _, _>).LoadRequiredStream id
      let stream: StreamContainerDto<_, _, _> = stream |> Expect.wantOk "Expected ok"
      let streamId = stream.Id
      let events = stream.Events
      printf "\nEvents\n%A\n" events

      let s1 = stream.Stream
      let modifiedTimestamp = s1.Modified
      let createdTimestamp = s1.Created
      printf "\nStream\n%A\n" s1

      // todo: check timestamp
      expect <@ streamId = id @>
      expect <@ createdTimestamp = t0 @>
      expect <@ modifiedTimestamp = t1 @>
      expect <@ events.Length = 2 @>
      expect <@ s1.Version = 2L @>
    }

    testTask "Applying multiple commands should update stream" {
      let store = store []
      let id =
        Guid.Parse "45465935-b61e-478a-bf89-2c86816e5588" |> TheaterStreamId.FromRaw
      let storage, ctx = createContext id
      do! store.ApplyCommand(storage, id, TheaterCommand.New(Create "hello world 1", ts), ctx)
      do! store.ApplyCommand(storage, id, TheaterCommand.New(Update "hello world 2"), ctx)
      do! store.ApplyCommand(storage, id, TheaterCommand.New(Update "hello world 3"), ctx)
      let! stream = (storage :> IEventStorage<_, _, _, _, _>).LoadRequiredStream id
      let stream: StreamContainerDto<_, _, _> = stream |> Expect.wantOk "Expected ok"
      let streamId = stream.Id
      let stream = stream.Stream
      expect <@ streamId = id @>
      expect <@ stream.Version = 3L @>
    }

    testTask "Applying multiple commands should create events 3" {
      let store = store []

      let id =
        Guid.Parse "f03606f0-a8f5-428b-8a38-6e5d77384887" |> TheaterStreamId.FromRaw

      let storage, ctx = createContext id
      do! store.ApplyCommand(storage, id, TheaterCommand.New(Create "hello world 1", ts), ctx)
      do! store.ApplyCommand(storage, id, TheaterCommand.New(Update "hello world 2"), ctx)
      do! store.ApplyCommand(storage, id, TheaterCommand.New(Update "hello world 3"), ctx)

      let! events = storage.LoadAllEvents id

      let events = events |> extractVersionAndDetails

      expect
        <@
          events = [
            {|
              Version = 1L
              Data = TheaterEventDetails.Created "hello world 1"
            |}
            {|
              Version = 2L
              Data = TheaterEventDetails.Updated "hello world 2"
            |}
            {|
              Version = 3L
              Data = TheaterEventDetails.Updated "hello world 3"
            |}
          ]
        @>

    }

    testTask "Applying multiple commands should create events 2" {
      let store = store []
      let id =
        Guid.Parse "7c5af6e2-01c6-474d-a95b-7aeb0dbe2bae" |> TheaterStreamId.FromRaw
      let storage, ctx = createContext id

      do! store.ApplyCommand(storage, id, TheaterCommand.New(Create "hello world"), ctx)
      do! store.ApplyCommand(storage, id, TheaterCommand.New(Update "hello world 2"), ctx)

      let! events = storage.LoadAllEvents id
      // printfn "Events\n%A" events
      let events = events |> extractVersionAndDetails

      expect
        <@
          events = [
            {|
              Version = 1L
              Data = TheaterEventDetails.Created "hello world"
            |}
            {|
              Version = 2L
              Data = TheaterEventDetails.Updated "hello world 2"
            |}
          ]
        @>
    }

    testTask "initialising command should create event" {
      let store = store []
      let id =
        Guid.Parse "533b2770-e6c1-40ac-b985-bc9541f937aa" |> TheaterStreamId.FromRaw
      let storage, ctx = createContext id
      let id = Guid.NewGuid() |> TheaterStreamId.FromRaw
      do! store.ApplyCommand(storage, id, TheaterCommand.New(Create "hello world"), ctx)

      let! events = storage.LoadAllEvents id

      let events = events |> extractVersionAndDetails
      expect
        <@
          events = [
            {|
              Version = 1L
              Data = TheaterEventDetails.Created "hello world"
            |}
          ]
        @>
    }

    testTask "initialising command should not error" {
      let store = store []
      let id =
        Guid.Parse "81ef2928-8f94-4306-b0be-9bc3b05338c8" |> TheaterStreamId.FromRaw
      let storage, ctx = createContext id
      let! result1 = store.ApplyCommand(storage, id, TheaterCommand.New(Create "hello world"), ctx)
      result1 |> Expect.isOk "Expected ok"
    }

    testTask "non initialising command should error" {
      let store = store []
      let id =
        Guid.Parse "2aa36167-0a49-489c-b5df-b24ecf7ef026" |> TheaterStreamId.FromRaw
      let storage, ctx = createContext id
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
              Details = EventStoreErrorDetails.InitializationError InitializationError.CommandIsNotInitializer
            }
          )
        @>
    }

    testTask "Deletion event should mark stream as deleted" {
      let store = store []
      let storage, ctx = createContext id
      let id =
        Guid.Parse "b5a03c1e-daed-4b76-ad4c-5fbe9fcd3326" |> TheaterStreamId.FromRaw
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
      let store = store []
      let storage, ctx = createContext id
      let id =
        Guid.Parse "b5a03c1e-daed-4b76-ad4c-5fbe9fcd3326" |> TheaterStreamId.FromRaw
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
      let store = store []
      let storage, ctx = createContext id
      let id =
        Guid.Parse "b5a03c1e-daed-4b76-ad4c-5fbe9fcd3326" |> TheaterStreamId.FromRaw
      do! store.ApplyEvents(storage, id, [ TheaterEventDetails.Created "Hello World" ], ctx)
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
      let store = store []
      let storage, ctx = createContext id

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
      let head = head |> versionAndDetails
      let head2 = head2 |> versionAndDetails

      expect
        <@
          head = {|
                   Version = 1L
                   Data = TheaterEventDetails.Created "Hello World 1"
                 |}
        @>
      expect
        <@
          head2 = {|
                    Version = 1L
                    Data = TheaterEventDetails.Created "Hello World 2"
                  |}
        @>
    }

    testTask "initialising event should be persisted" {
      let store = store []
      let storage, ctx = createContext id
      let id =
        Guid.Parse "b5a03c1e-daed-4b76-ad4c-5fbe9fcd3326" |> TheaterStreamId.FromRaw
      let evt = TheaterEventDetails.Created "Hello World"
      do! store.ApplyEvents(storage, id, [ evt ], ts, ctx)
      let! events = storage.LoadAllEvents id
      let events = events |> Expect.wantSome "Expected some events"
      let head =
        events
        |> Expect.wantFirst "ExpectAddEventStoreed at least one event"
        |> versionAndDetails
      expect
        <@
          head = {|
                   Version = 1L
                   Data = evt
                 |}
        @>
    }

    // testTask "initialising event should not error" {
    //   let store = store []
    //   let storage, ctx = createContext id
    //   let id =
    //     Guid.Parse "0041e429-c8b6-48ab-b7b7-2151940ff8bf" |> TheaterStreamId.FromRaw
    //   let! result1 = store.ApplyEvents(storage, id, [ TheaterEventDetails.Created "" ], ts, ctx)
    //   let! _ = store.ApplyEvents(storage, id, [ TheaterEventDetails.Created "" ], ts, ctx)
    //   // let resultIsOk = result1.IsOk
    //   expect <@ result1.IsOk @>
    // }

    // testTask "non initialising event should error" {
    //   let store = store []
    //   let storage,ctx = createContext id
    //   let id =
    //     Guid.Parse "dc41f5f7-46aa-4406-99b2-61510d549b4f" |> TheaterStreamId.FromRaw
    //   let! result1 = store.ApplyEvents(storage, id, [ TheaterEventDetails.Deleted ], ts, ctx)
    //   let error = result1 |> Expect.wantError "Expected error"
    //   expect <@ error.Details = EventStoreErrorDetails.InitializationError InitializationError.EventIsNotInitializer @>
    // }

    testTask "empty events should error" {
      let store = store []
      let storage, ctx = createContext id
      let id =
        Guid.Parse "07b23a15-c365-4391-9e27-2413066a42c9" |> TheaterStreamId.FromRaw
      let! _ = task { return 1 }

      let! result1 = store.ApplyEvents(storage, id, [], ts, ctx)
      result1 |> Expect.isError "expected ok"

      result {
        let! x = result1
        return x
      }
      |> fun _ -> Expect.isOk "" |> ignore

    }

  ]
