module Theater.Tests

open TinyEventStore.Check
open Expecto
open Expecto.Flip
open System
open Decider
open TinyEventStore.Interfaces
open TinyEventStore.EfStorage
open Microsoft.EntityFrameworkCore

[<RequireQualifiedAccess>]
type TheaterStreamId =
  | TheaterStreamId of Guid

  static member New() = TheaterStreamId(Guid.NewGuid())
  static member ToRaw(TheaterStreamId id) = id
  static member FromRaw(id) = TheaterStreamId(id)
  static member Converter = TheaterStreamId.ToRaw, TheaterStreamId.FromRaw

[<RequireQualifiedAccess>]
type TheaterEventId =
  | TheaterEventId of Guid

  static member New() = TheaterEventId(Guid.NewGuid())
  static member ToRaw(TheaterEventId id) = id
  static member FromRaw(id) = TheaterEventId(id)
  static member Converter = TheaterEventId.ToRaw, TheaterEventId.FromRaw

let createStream: StreamCreator<_, _, _, _> =
  fun appendEventsResult ->
    {
      Version = appendEventsResult.Version
      Created = appendEventsResult.TimeStamp
      Updated = appendEventsResult.TimeStamp
      Name = ""
    }

let updateStream: StreamUpdater<_, _, _, _> =
  fun stream0 appendEventsResult ->
    { stream0 with
        Version = appendEventsResult.Version
        Updated = appendEventsResult.TimeStamp
        Name = ""
    }

// let inmemStorage =
//   // InmemEventStorage.newStorage<Guid, TheaterStream, TheaterState, TheaterEvent, TheaterCommand> (
//   InmemEventStorage.newStorage<_, _, _, _, _> (createStream, updateStream)

type Discriminator =
  | Theater = 1

// let streamConverter:Converter<TheaterStream,_> fun x ->

let efStorageOptions: EventStorageOptions<_, _, _, _> =
  {
    // Id = TheaterEventId.Converter
    StreamId = TheaterStreamId.Converter
    //todo
    Stream =
      (fun (stream, id, version) -> Dtos.StreamDto(Id = (id |> TheaterStreamId.ToRaw), Version = version)),
      (fun x ->
        {
          TheaterStream.Version = x.Version
          Created = x.Created
          Updated = x.Modified
          Name = ""
        },
        x.Id |> TheaterStreamId.FromRaw,
        x.Version
      )
    Event =
      (fun (event: TheaterEvent, id, version) ->
        Dtos.EventDto(
          StreamId = (id |> TheaterStreamId.ToRaw),
          Version = version,
          Data = event,
          Timestamp = event.TimeStamp
        )
      ),
      (fun x ->
        {
          TheaterEvent.Version = x.Version
          TimeStamp = x.Timestamp
          Data = x.Data.Data
        },
        x.StreamId |> TheaterStreamId.FromRaw,
        x.Version
      )
    TableNamePrefix = "theater"
  }

type EventDbContext(options) =
  inherit DbContext(options)

  // member this.GetStorage(system: System<_, _, _, _>) = ()

  override _.OnModelCreating(modelBuilder: ModelBuilder) : unit =
    let _ =
      modelBuilder.AddSharedEventStorage<Guid, Guid, Discriminator>(
        "theater_shared",
        fun o ->
          o.WithStreamType<TheaterStreamId, Guid, TheaterEventId, Guid, TheaterStream, TheaterEvent>(
            Discriminator.Theater,
            efStorageOptions,
            None
          )
      )
    ()


let storage (name) =
  let options = DbContextOptionsBuilder<EventDbContext>()
  options
    .UseSqlite($"Data Source=test.{name}.sqlite")
    .EnableSensitiveDataLogging()
    .EnableDetailedErrors()
  |> ignore
  let db = new EventDbContext(options.Options)
  db.Database.EnsureDeleted() |> ignore
  db.Database.EnsureCreated() |> ignore

  EfStorage<TheaterStreamId, Guid, TheaterStream, TheaterState, TheaterEvent, TheaterCommand, EventDbContext>(
    createStream,
    updateStream,
    db,
    efStorageOptions
  )

let printSubscription: Subscription<_, _, _> =
  fun x ->
    task {
      printfn "subscription %A" x
      return ()
    }

let ts = DateTimeOffset.Parse("2025-02-02 10:15:00")

let storeAndStorage (subscription) =
  let storage = storage (Guid.NewGuid())
  EventStore(
    system,
    storage,
    // fun (ctx: EventContext) (details: TheaterEventDetails) ->
    (fun ctx details ->
      {
        Version = ctx.Version
        TimeStamp = ts
        Data = details
      }
    ),
    subscription
  // [ subscription ]
  ),
  storage

let store () = storeAndStorage ([]) |> fst
let storeWithSubscription (subscription) = storeAndStorage (subscription) |> fst

open FsToolkit.ErrorHandling

module Expect =
  let wantFirst msg l =
    match l with
    | [] -> failtest msg
    | head :: _ -> head

let tests =
  [
    testTask "subscription should be invoked" {
      let result = ResizeArray()
      let subscription: Subscription<_, _, _> =
        fun x ->
          task {
            result.Add x
            return ()
          }
      let store = storeWithSubscription ([ subscription ])
      let id =
        Guid.Parse "e53991ef-6969-4012-94db-7a005e962e50" |> TheaterStreamId.FromRaw
      do! store.ApplyCommand(id, TheaterCommand.New(Create "hello world 1", ts))
      let result = result |> Seq.toList
      expect
        <@
          result = [
            {

              Id = id
              Version = 1UL
              State =
                {
                  Name = "hello world 1"
                  IsDeleted = false
                  TimeStamp = ts
                }
              Events =
                [
                  {
                    Version = 1UL
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
      let store, storage = storeAndStorage ([])

      let id =
        Guid.Parse "f03606f0-a8f5-428b-8a38-6e5d77384887" |> TheaterStreamId.FromRaw

      do! store.ApplyCommand(id, TheaterCommand.New(Create "hello world 1", ts))
      do! store.ApplyCommand(id, TheaterCommand.New(Update "hello world 2"))
      do! store.ApplyCommand(id, TheaterCommand.New(Update "hello world 3"))

      let! events = storage.LoadAllEvents id
      printfn "Events\n%A" events

      expect
        <@
          events = Some
            [
              {
                Version = 1UL
                TimeStamp = ts
                Data = TheaterEventDetails.Created "hello world 1"
              }
              {
                Version = 2UL
                TimeStamp = ts
                Data = TheaterEventDetails.Updated "hello world 2"
              }
              {
                Version = 3UL
                TimeStamp = ts
                Data = TheaterEventDetails.Updated "hello world 3"
              }
            ]
        @>
    }

    testTask "Applying multiple commands should create events 2" {
      let store, storage = storeAndStorage ([])
      let id = Guid.NewGuid() |> TheaterStreamId.FromRaw

      printfn "send one command"
      do! store.ApplyCommand(id, TheaterCommand.New(Create "hello world"))
      printfn "send another command"
      do! store.ApplyCommand(id, TheaterCommand.New(Update "hello world 2"))

      let! events = storage.LoadAllEvents id
      printfn "Events\n%A" events

      expect
        <@
          events = Some
            [
              {
                Version = 1UL
                TimeStamp = ts
                Data = TheaterEventDetails.Created "hello world"
              }
              {
                Version = 2UL
                TimeStamp = ts
                Data = TheaterEventDetails.Updated "hello world 2"
              }
            ]
        @>
    }

    testTask "initialising command should create event" {
      let store, storage = storeAndStorage ([])
      let id = Guid.NewGuid() |> TheaterStreamId.FromRaw
      do! store.ApplyCommand(id, TheaterCommand.New(Create "hello world"))

      let! events = storage.LoadAllEvents id

      expect
        <@
          events = Some
            [
              {
                Version = 1UL
                TimeStamp = ts
                Data = TheaterEventDetails.Created "hello world"
              }
            ]
        @>
    }

    testTask "initialising command should not error" {
      let store = store ()
      let id = Guid.NewGuid() |> TheaterStreamId.FromRaw
      let! result1 = store.ApplyCommand(id, TheaterCommand.New(Create "hello world"))
      result1 |> Expect.isOk "Expected ok"
    }

    testTask "non initialising command should error" {
      let store = store ()
      let id = Guid.NewGuid() |> TheaterStreamId.FromRaw
      let! result1 = store.ApplyCommand(id, TheaterCommand.New(Update "hello world"))
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
      let store = store ()
      let id =
        Guid.Parse("b5a03c1e-daed-4b76-ad4c-5fbe9fcd3326") |> TheaterStreamId.FromRaw
      do!
        store.ApplyEvents(
          id,
          [
            TheaterEventDetails.Created "Hello World"
            TheaterEventDetails.Updated "Hello World 2"
          ],
          ts
        )
      let! hydrationResult = store.Rehydrate id
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
      let store = store ()
      let id =
        Guid.Parse("b5a03c1e-daed-4b76-ad4c-5fbe9fcd3326") |> TheaterStreamId.FromRaw
      do!
        store.ApplyEvents(
          id,
          [
            TheaterEventDetails.Created "Hello World"
            TheaterEventDetails.Updated "Hello World 2"
          ],
          ts
        )
      let! hydrationResult = store.Rehydrate id
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
      let store = store ()
      let id =
        Guid.Parse("b5a03c1e-daed-4b76-ad4c-5fbe9fcd3326") |> TheaterStreamId.FromRaw
      do! store.ApplyEvents(id, [ TheaterEventDetails.Created "Hello World" ], ts)
      let! hydrationResult = store.Rehydrate id
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
    testTask "initialising event should be persisted" {
      let store, storage = storeAndStorage ([])
      let id =
        Guid.Parse("b5a03c1e-daed-4b76-ad4c-5fbe9fcd3326") |> TheaterStreamId.FromRaw
      let evt = TheaterEventDetails.Created "Hello World"
      do! store.ApplyEvents(id, [ evt ], ts)
      let! events = storage.LoadAllEvents id
      let events = events |> Expect.wantSome "Expected some events"
      let head = events |> Expect.wantFirst "ExpectAddEventStoreed at least one event"
      expect
        <@
          head = {
                   TheaterEvent.Version = 1UL
                   TimeStamp = ts
                   Data = evt
                 }
        @>
    }
    testTask "initialising event should not error" {
      let store = store ()
      let id =
        Guid.Parse("0041e429-c8b6-48ab-b7b7-2151940ff8bf") |> TheaterStreamId.FromRaw
      let! result1 = store.ApplyEvents(id, [ TheaterEventDetails.Created "" ], ts)
      do! store.ApplyEvents(id, [ TheaterEventDetails.Created "" ], ts)
      expect <@ result1 = Ok() @>
    }
    testTask "non initialising event should error" {
      let store = store ()
      let id =
        Guid.Parse("dc41f5f7-46aa-4406-99b2-61510d549b4f") |> TheaterStreamId.FromRaw
      let! result1 = store.ApplyEvents(id, [ TheaterEventDetails.Deleted ], ts)
      let error = result1 |> Expect.wantError "Expected error"
      expect <@ error.Details = EventStoreErrorDetails.InitializationError(InitializationError.EventIsNotInitializer) @>
    }
    testTask "empty events should error" {
      let store = store ()
      let id =
        Guid.Parse("07b23a15-c365-4391-9e27-2413066a42c9") |> TheaterStreamId.FromRaw
      let! x = task { return 1 }

      let! result1 = store.ApplyEvents(id, [], ts)
      result1 |> Expect.isError "expected ok"

      result {
        let! x = result1
        return x
      }
      |> fun x -> Expect.isOk "" |> ignore

    }

  ]
