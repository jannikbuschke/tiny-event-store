namespace TinyEventStore.Interfaces

open FsToolkit.ErrorHandling
open System

// [<RequireQualifiedAccess>]
// type CausationId =
//   | CommandId of CommandId
//   | EventId of EventId

// type Causation =
//   | Command of Entity:string * CommandName:string * TinyEventStore.CommandId
//   | Event of Entity:string * EventName:string * TinyEventStore.EventId
//   | DomainEvent of Name:string

type V = int64
type SequenceId = int64

module Version =
  let V1 = 1L
  let Zero = 0L

module SequenceId =
  let V1 = 1L
  let Zero = 0L

type IEvent =
  abstract member Version: V
  abstract member TimeStamp: DateTimeOffset

type IStream =
  abstract member Version: V
  abstract member Modified: DateTimeOffset
  abstract member Created: DateTimeOffset

type ICommand =
  abstract member TimeStamp: DateTimeOffset
  abstract member Id: TinyEventStore.CommandId

type Evolve<'s, 'e> = 's -> 'e -> 's
// todo, change to result
type Decide<'s, 'c, 'e> = 's -> 'c -> 'e list
type IsDeleted<'s, 'e> = 's -> 'e -> bool
type IsInitialiser<'m> = 'm -> bool

type Projection<'s, 'e> = Evolve<'s, 'e>

type Aggregate<'s, 'e> =
  {
    zero: 's
    evolve: Evolve<'s, 'e>
    // IsDeleting
    isDeleted: IsDeleted<'s, 'e>
  }

type System<'state, 'e, 'ed, 'c> =
  {
    aggregate: Aggregate<'state, 'e>
    decide: Decide<'state, 'c, 'ed>
    isEventInitialiser: IsInitialiser<'ed>
    isCommandInitializer: IsInitialiser<'c>
  }

open System.Threading.Tasks

[<RequireQualifiedAccess>]
type InitializationError =
  | EventIsNotInitializer
  | CommandIsNotInitializer
  | Other

[<RequireQualifiedAccess>]
type IllegalArgumentError = | EmptyEvents

[<RequireQualifiedAccess>]
type EventStoreErrorDetails =
  | NotInitialized
  | NotFound
  | IllegalArgument of IllegalArgumentError
  | InitializationError of InitializationError
  | CommitError

type EventStoreError =
  {
    Message: string option
    Details: EventStoreErrorDetails
  }

  static member New(details, message) =
    {
      Message = message
      Details = details
    }

type ApplyResult = Task<Result<unit, EventStoreError>>

type AppendEventsResult<'id, 'state, 'e> =
  {
    Id: 'id
    Version: V
    State: 'state
    Events: 'e list
    IsNew: bool
    TimeStamp: DateTimeOffset
  // CausationId: CausationId option
  }

type IProjection =
  abstract member Apply: AppendEventsResult<'id, 'state, 'e> -> Task

[<Struct>]
type StreamContainerDto<'id, 'stream, 'e> =
  {
    Id: 'id
    Stream: 'stream
    Events: 'e list
  }

type IEventStorage<'streamId, 'stream, 'state, 'event, 'c when 'stream :> IStream> =
  abstract member LoadEventRange: 'streamId * V * V -> Task<'event list option>
  abstract member LoadEventsFrom: 'streamId * V -> Task<'event list option>
  abstract member LoadAllEvents: 'streamId -> Task<'event list option>
  abstract member LoadStream: 'streamId -> Task<StreamContainerDto<'streamId, 'stream, 'event> option>
  abstract member LoadRequiredStream:
    'streamId -> Task<Result<StreamContainerDto<'streamId, 'stream, 'event>, EventStoreError>>
  abstract member QueryStreams: unit -> Task<Result<('streamId * 'stream) list, EventStoreError>>
  abstract member Commit: AppendEventsResult<'streamId, 'state, 'event> -> Task<Result<unit, EventStoreError>>

type HydrationResult<'s, 'stream, 'e when 'stream :> IStream> =
  {
    State: 's
    Stream: 'stream
    Events: 'e list
  }

  member this.IsZero() = this.Stream.Version = Version.Zero

type EventContext<'streamId> =
  {
    StreamId: 'streamId
    EventId: TinyEventStore.EventId
    Version: V
    TimeStamp: DateTimeOffset
    Causation: TinyEventStore.Causation option
  }

type WrapEventDetails<'streamId, 'ed, 'e> = EventContext<'streamId> -> 'ed -> 'e

type OnCommittingEventHandler<'a, 'b, 'c, 'ctx> = 'ctx -> AppendEventsResult<'a, 'b, 'c> -> Task

module Core =
  open TinyEventStore.Log

  let logger =
    LogProvider.getLoggerByName "TinyEventStore.CoreModule"

  let error details message =
    Error(EventStoreError.New(details, Some message))

  let error2 details =
    Error(EventStoreError.New(details, None))

  let aggregateEvents evolve state events = events |> Seq.fold evolve state

  let aggregateWithWrapping evolve state event wrapper =
    let s1 = evolve state event
    s1, wrapper event

  let applyEventsFromZero (aggregate: Aggregate<_, _>) events =
    events |> aggregateEvents aggregate.evolve aggregate.zero

  let rehydrate
    (aggregate: Aggregate<'state, 'e>)
    (store: IEventStorage<'id, 'stream, 'state, 'e, 'c>)
    id
    : Task<HydrationResult<_, _, _> option>
    =
    task {
      logger.info (Log.setMessage "Rehydrating stream {stream_id}" >> Log.addParameter id)
      let! stream = store.LoadStream id
      let result =
        stream
        |> Option.map (fun stream ->
          let state = stream.Events |> applyEventsFromZero aggregate
          {
            State = state
            Stream = stream.Stream
            Events = stream.Events
          }
        )

      logger.debug (
        Log.setMessage "Rehydration result {rehydration_result}"
        >> Log.addParameter result
      )
      return result
    }

  let getStateAndVersion (system: System<_, _, _, _>) (hydrationResult: HydrationResult<_, _, _> option) =
    hydrationResult
    |> Option.map (fun x -> x.State, x.Stream.Version)
    |> Option.defaultValue (system.aggregate.zero, Version.Zero)

  let appendEvents
    (system: System<'state, 'e, 'ed, 'c>)
    (store: IEventStorage<'id, 'stream, 'state, 'e, 'c>)
    (onCommitting: OnCommittingEventHandler<_, _, _, _> seq)
    (f: WrapEventDetails<'id, 'ed, 'e>)
    (ts: DateTimeOffset)
    (id: 'id)
    (events: 'ed list)
    (ctx: 'ctx)
    (causation: TinyEventStore.Causation option)
    =
    logger.debug (Log.setMessage "Appending events")

    match events with
    | [] ->
      error2 (EventStoreErrorDetails.IllegalArgument IllegalArgumentError.EmptyEvents)
      |> Task.FromResult
    | head :: events ->
      let allEvents = head :: events

      let errorIfNoStreamAndNotInitializingEvent (hydrationResult: HydrationResult<_, _, _> option) =
        if hydrationResult = None && not (head |> system.isEventInitialiser) then
          error
            (EventStoreErrorDetails.InitializationError InitializationError.EventIsNotInitializer)
            (sprintf
              "Expected an initialising event (%s), as the event stream %s does not yet have any events"
              (head.GetType().Name)
              (id.ToString()))
        else
          Ok hydrationResult

      let evolve (v0: V, state0: 'state, evts: 'e list) (evt: 'ed) =
        let v1 = v0 + 1L
        let ctx =
          {
            StreamId = id
            Version = v1
            TimeStamp = ts
            EventId = TinyEventStore.EventId.New()
            Causation = causation
          }
        if evts.Length <> int v0 then
          logger.warn (
            Log.setMessage "Expected {expected} events, but got {actual}"
            >> Log.addParameter v0
            >> Log.addParameter evts.Length
          )

        let wrappedEvent = evt |> f ctx
        let state1 = system.aggregate.evolve state0 wrappedEvent
        logger.info (
          Log.setMessage "Evolve {id} {v0}->{v1}"
          >> Log.addParameter id
          >> Log.addParameter v0
          >> Log.addParameter v1
          >> Log.addContext "state0" state0
          >> Log.addContext "state1" state1
          >> Log.addContext "Events" evts
          >> Log.addContext "Wrapped event" wrappedEvent
        )
        // logDebug(, [|id;v0;state0;evts;wrappedEvent;state1|])
        ctx.Version, state1, evts @ [ wrappedEvent ]

      let handleAppendEvents (hydrationResult: HydrationResult<_, _, _> option) =
        let state0, version0 = getStateAndVersion system hydrationResult
        let version1, state1, events = allEvents |> List.fold evolve (version0, state0, [])
        logger.debug (
          Log.setMessage "Events appended {stream_id}"
          >> Log.addParameter id
          >> Log.addContext "version0" version0
          >> Log.addContext "version1" version1
          >> Log.addContext "state0" state0
          >> Log.addContext "state1" state1
          >> Log.addContext "events" events
          >> Log.addContext "all_events" allEvents
        )
        {
          Id = id
          Version = version1
          State = state1
          Events = events
          IsNew = hydrationResult.IsNone
          TimeStamp = ts
        }

      let commitNewEvents (appendEventsResult: AppendEventsResult<_, _, _>) =
        taskResult {
          // printfn "append events result\n%A" appendEventsResult
          for handler in onCommitting do
            do! handler ctx appendEventsResult
          let causationName = causation |> Option.map _.MessageName
          logger.debug (Log.setMessage "Committing events")
          do! store.Commit appendEventsResult
          logger.info (
            Log.setMessage "Committed: {causation} ==>> {eventResult}"
            >> Log.addParameter causationName
            >> Log.addParameter appendEventsResult
          )
          return appendEventsResult
        }

      id
      |> rehydrate system.aggregate store
      |> Task.bind (
        errorIfNoStreamAndNotInitializingEvent
        >> Result.map handleAppendEvents
        >> Result.map commitNewEvents
        >> Result.sequenceTask
      )

  let applyCommand
    (system: System<'state, 'e, 'ed, 'c>)
    (storage: IEventStorage<'id, 'stream, 'state, 'e, 'c>)
    (onCommitting: OnCommittingEventHandler<_, _, _, _> seq)
    (f: WrapEventDetails<'id, 'ed, 'e>)
    (id: 'id)
    (c: 'c :> ICommand)
    (ctx: 'ctx)
    =
    taskResult {
      let causation: TinyEventStore.Causation =
        {
          EntityType = None // typeof<'state>.FullName |> Some
          MessageName = c.GetType().FullName
          Id = c.Id |> TinyEventStore.CausationId.CommandId |> Some
        }
      let! rehydrationResult = rehydrate system.aggregate storage id
      let state0, _ = getStateAndVersion system rehydrationResult
      do!
        (rehydrationResult.IsSome || system.isCommandInitializer c)
        |> Result.requireTrue (

          EventStoreError.New(
            EventStoreErrorDetails.InitializationError InitializationError.CommandIsNotInitializer,
            sprintf
              "Expected an initialising command (but got (%s) which is not defined as an initializer), as the env stream %s does not yet have any events"
              (c.GetType().Name)
              (id.ToString())
            |> Some
          )
        )

      let events = system.decide state0 c
      return! appendEvents system storage onCommitting f c.TimeStamp id events ctx (Some causation)
    }

type Subscription<'id, 'state, 'e, 'ctx> = 'ctx -> AppendEventsResult<'id, 'state, 'e> -> Task<unit>

type EventStore<'id, 'stream, 'state, 'e, 'c, 'ed, 'ctx
  when 'state: equality
  and 'stream :> IStream
  and 'stream: equality
  and 'e: equality
  and 'e :> IEvent
  and 'c :> ICommand>
  (
    system: System<'state, 'e, 'ed, 'c>,
    wrapper: WrapEventDetails<'id, 'ed, 'e>,
    onCommitting: OnCommittingEventHandler<_, _, _, 'ctx> seq,
    subscriptions: Subscription<'id, 'state, 'e, 'ctx> list
  ) =
  member _.Rehydrate(store: IEventStorage<_, _, _, _, _>, id) =
    Core.rehydrate system.aggregate store id

  member _.ApplyEvents
    (
      store: IEventStorage<_, _, _, _, _>,
      id,
      eventDetails: 'ed list,
      ts,
      ctx: 'ctx,
      ?causation: TinyEventStore.Causation
    ) =
    taskResult {
      let! result = Core.appendEvents system store onCommitting wrapper ts id eventDetails ctx causation

      let! result = result
      for subscription in subscriptions do
        do! subscription ctx result
      return result
    }

  member this.ApplyEvents
    (store: IEventStorage<_, _, _, _, _>, id, eventDetails: 'ed list, ctx: 'ctx, ?causation: TinyEventStore.Causation)
    =
    match causation with
    | Some causation -> this.ApplyEvents(store, id, eventDetails, DateTimeOffset.UtcNow, ctx, causation)
    | None -> this.ApplyEvents(store, id, eventDetails, DateTimeOffset.UtcNow, ctx)

  member _.ApplyCommand(store: IEventStorage<_, _, _, _, _>, id, command, ctx: 'ctx) =
    taskResult {
      let! result = Core.applyCommand system store onCommitting wrapper id command ctx
      let! result = result
      for subscription in subscriptions do
        do! subscription ctx result
    }

type StreamCreator<'id, 'stream, 'state, 'e when 'stream :> IStream> = AppendEventsResult<'id, 'state, 'e> -> 'stream

type StreamUpdater<'id, 'stream, 'state, 'e when 'stream :> IStream> =
  'stream -> AppendEventsResult<'id, 'state, 'e> -> 'stream

module InmemEventStorage =

  open System.Collections.Generic

  let newStorage<'id, 'stream, 'state, 'e, 'c when 'id: equality and 'stream :> IStream and 'e :> IEvent>
    (streamCreator: StreamCreator<_, _, _, _>, streamUpdater: StreamUpdater<_, _, _, _>)
    =

    let dict = Dictionary<'id, 'stream * ResizeArray<'e>>()

    let tryGetVal (dict: IDictionary<_, _>) key =
      match dict.TryGetValue key with
      | true, v -> Some v
      | false, _ -> None

    let getEvents id =
      id |> tryGetVal dict |> Option.map snd |> Option.map Seq.toList

    let getStream id = id |> tryGetVal dict |> Option.map fst

    let getEventStream id = id |> tryGetVal dict

    { new IEventStorage<'id, 'stream, 'state, 'e, 'c> with

        member _.LoadEventRange(id, from: V, until: V) =
          id
          |> getEvents
          |> Option.map (List.skip (int (from - 1L)))
          |> Option.map (List.take (int (until - from)))
          |> Task.FromResult

        member _.QueryStreams() =
          dict
          |> Seq.map (fun id -> id.Key, id.Value |> fst)
          |> Seq.toList
          |> Ok
          |> Task.FromResult

        member _.LoadEventsFrom(id, from) =
          id |> getEvents |> Option.map (List.skip (int (from - 1L))) |> Task.FromResult

        member _.LoadAllEvents id =
          id |> getEvents |> Option.map (List.skip 0) |> Task.FromResult

        member _.LoadRequiredStream id =
          taskResult {
            let events, stream = id |> getEvents, id |> getStream
            let! events =
              events
              |> Result.requireSome
                {
                  Message = Some "Events not found"
                  Details = EventStoreErrorDetails.NotFound
                }
            let! stream =
              stream
              |> Result.requireSome
                {
                  Message = Some "Stream not found"
                  Details = EventStoreErrorDetails.NotFound
                }
            return
              {
                Id = id
                Stream = stream
                Events = events
              }
          }

        member _.LoadStream id =
          let events, stream = id |> getEvents, id |> getStream
          Option.map2
            (fun x y ->
              {
                Id = id
                Stream = y
                Events = x
              }
            )
            events
            stream
          |> Task.FromResult

        member _.Commit v =
          let existingStream =
            match v.Id |> getEventStream with
            | Some x -> x
            | None ->
              let list = ResizeArray()
              let s = streamCreator v
              s, list

          let existingStream, existingEvents = existingStream
          v.Events |> Seq.iter existingEvents.Add
          // let lastEvent = v.Events |> Seq.last
          let stream1 = streamUpdater existingStream v
          dict[v.Id] <- stream1, existingEvents

          () |> Ok |> Task.FromResult
    }
