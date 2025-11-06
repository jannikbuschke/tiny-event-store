module TinyEventStore.InterfacesSimple.Core

open TinyEventStore.Log
open System
open FsToolkit.ErrorHandling
open System.Threading.Tasks


let private logger = LogProvider.getLoggerByName "TinyEventStore.CoreModule"

let private error details message =
  Error(EventStoreError.New(details, Some message))

let private error2 details =
  Error(EventStoreError.New(details, None))

let aggregateEvents evolve state events = events |> Seq.fold evolve state

let aggregateWithWrapping evolve state event wrapper =
  let s1 = evolve state event
  s1, wrapper event

let applyEventsFromZero (aggregate: Aggregate<_, _>) events =
  events |> aggregateEvents aggregate.evolve aggregate.zero

let rehydrate
  (aggregate: Aggregate<'state, 'e>)
  (store: ISimpleEventStorage<'e>)
  (getVersion: 'e -> V)
  (id: Guid)
  : Task<HydrationResult<_, _>>
  =
  task {
    logger.info (Log.setMessage "Rehydrating stream {stream_id}" >> Log.addParameter id)
    let! events = store.LoadAllEvents id
    let result =
      match events with
      | Some events ->
        let state = events.List |> applyEventsFromZero aggregate
        HydrationResult.Value
          {
            State = state
            Events = events
            Version = events.Last() |> getVersion
          }
      | None -> HydrationResult.NotStarted

    logger.debug (
      Log.setMessage "Rehydration result {rehydration_result}"
      >> Log.addParameter result
    )
    return result
  }

let createHandler
  (system: EventStoreDefinition<_, _, _>)
  (onCommitting: OnCommittingEventHandler<_, _, 'ctx> seq)
  (ctx: 'ctx)
  =
  let getStateAndVersion (hydrationResult: HydrationResult<_, _>) =
    match hydrationResult with
    | HydrationResult.NotStarted -> system.aggregate.zero, V.zero
    | HydrationResult.Value v -> v.State, v.Version

  // let evolve (id: Guid) (v0: V, state0: 'state, evts: 'e list) (evt: 'e) =
  //   let v1 = v0 + 1L
  //   if evts.Length <> int v0 then
  //     logger.warn (
  //       Log.setMessage "Expected {expected} events, but got {actual}"
  //       >> Log.addParameter v0
  //       >> Log.addParameter evts.Length
  //     )
  //   // let wrappedEvent = evt |> f ctx
  //   let state1 = system.aggregate.evolve state0 evt
  //   logger.info (
  //     Log.setMessage "Evolve {id} {v0}->{v1}"
  //     >> Log.addParameter id
  //     >> Log.addParameter v0
  //     >> Log.addParameter v1
  //     >> Log.addContext "state0" state0
  //     >> Log.addContext "state1" state1
  //     >> Log.addContext "Events" evts
  //     // >> Log.addContext "Wrapped event" wrappedEvent
  //   )
  //   // logDebug(, [|id;v0;state0;evts;wrappedEvent;state1|])
  //   v1, state1, evts @ [ evt ]

  let handleAppendEvents
    ts
    (id: Guid)
    (createEvents: CreateEventsContext -> Result<NonEmptyList<'e>, string>)
    (hydrationResult: HydrationResult<_, _>)
    : Result<AppendEventsResult<_, _>, EventStoreError>
    =
    result {
      if id = Guid.Empty then
        failwith "id is empty"
      let state0, version0 = getStateAndVersion hydrationResult
      let ctx =
        {
          CreateEventsContext.StreamId = id
          TimeStamp = ts
          Version = version0
        }

      let! events =
        createEvents ctx
        |> Result.mapError (fun msg -> EventStoreError.New(EventStoreErrorDetails.ConstructEventsError msg, None))
      do!
        (not hydrationResult.IsNotStarted || system.isEventInitialiser events.Head)
        |> Result.requireTrue (
          EventStoreError.New(
            EventStoreErrorDetails.InitializationError InitializationError.EventIsNotInitializer,
            None
          )
        )
      let state1 = events.List |> List.fold system.aggregate.evolve state0
      let version1 = events.Last() |> system.getEventVersion
      // let version1, state1, newEvents = eventsToApply.List |> List.fold (evolve system ts id causation) (version0, state0, [])
      logger.debug (
        Log.setMessage "Events appended {stream_id}"
        >> Log.addParameter id
        >> Log.addContext "version0" version0
        >> Log.addContext "version1" version1
        >> Log.addContext "state0" state0
        >> Log.addContext "state1" state1
        >> Log.addContext "events" events.List
      // >> Log.addContext "all_events" allEvents
      )
      if id = Guid.Empty then
        failwith "id empty"
      return
        {
          Id = id
          Version = version1
          State = state1
          Events = events
          IsNew = hydrationResult.IsNotStarted
          TimeStamp = ts
        }

    }

  let commitNewEvents (store: ISimpleEventStorage<_>) (appendEventsResult: AppendEventsResult<_, _>) =
    taskResult {
      for handler in onCommitting do
        do! handler ctx appendEventsResult
      // let causationName = causation |> Option.map _.MessageName
      logger.debug (Log.setMessage "Committing events")
      do! store.Commit appendEventsResult
      logger.info (
        Log.setMessage "Committed: {causation} ==>> {eventResult}"
        // >> Log.addParameter causationName
        >> Log.addParameter appendEventsResult
      )
      return appendEventsResult
    }

  let applyEvents
    (store: ISimpleEventStorage<_>)
    (streamId: Guid, ts: DateTimeOffset)
    (events: CreateEventsContext -> NonEmptyList<'e>)
    =
    taskResult {
      let! hydrationResult = rehydrate system.aggregate store system.getEventVersion streamId
      let events = events >> Ok
      let! x = handleAppendEvents ts streamId events hydrationResult
      let! commitResult = commitNewEvents store x
      if commitResult.Id = Guid.Empty then
        failwith "empty guid"
      return commitResult
    }

  let applyCommand
    (store: ISimpleEventStorage<_>)
    // (onCommitting: OnCommittingEventHandler<_, _, _, _> seq)
    (streamId: Guid, ts: DateTimeOffset) // DateTimeOffset or Version?
    (c: 'c)
    // (ctx: 'ctx)
    =
    taskResult {
      let! hydrationResult = rehydrate system.aggregate store system.getEventVersion streamId
      let! state0, version0 =
        match hydrationResult with
        | HydrationResult.NotStarted ->
          if not (system.isCommandInitializer c) then
            Error
              {
                Details = EventStoreErrorDetails.InitializationError InitializationError.CommandIsNotInitializer
                Message = None
              }
          else
            Ok(getStateAndVersion hydrationResult)
        | HydrationResult.Value _ -> Ok(getStateAndVersion hydrationResult)

      let ctx =
        {
          CreateEventsContext.StreamId = streamId
          TimeStamp = ts
          Version = version0
        }

      let! _ =
        system.decide ctx state0 c
        |> Result.mapError (fun msg -> EventStoreError.New(EventStoreErrorDetails.DecideError msg, None))

      let createEvents ctx = system.decide ctx state0 c
      let! x = handleAppendEvents ts streamId createEvents hydrationResult
      let! commitResult = commitNewEvents store x
      if commitResult.Id = Guid.Empty then
        failwith "empty guid"

      return commitResult

    }
  let rehydrate store streamId =
    rehydrate system.aggregate store system.getEventVersion streamId

  {|
    applyEvents = applyEvents
    applyCommand = applyCommand
    rehydrate = rehydrate
  |}
// applyEvents, applyCommand
