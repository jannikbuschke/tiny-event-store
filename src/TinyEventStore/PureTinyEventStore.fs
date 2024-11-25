module TinyEventStore.PureStore

open FsToolkit.ErrorHandling
open TinyEventStore

let rehydrateEvents<'id, 'state, 'event, 'header>
  (zero: 'state)
  (evolve: Evolve<'id, 'state, 'event, 'header>)
  (events: EventEnvelope<'id, 'event, 'header> seq)
  =
  let evolveWithVersionCheck (state, version) e =
    if e.Version <= version then
      failwith "Events are not ordered"

    let innerState = evolve state e
    (innerState, e.Version)

  let state, _ =
    events |> Seq.sortBy _.Version |> Seq.fold evolveWithVersionCheck (zero, 0u)

  state

let rehydrate<'id, 'state, 'event, 'header>
  (zero: 'state)
  (evolve: Evolve<'id, 'state, 'event, 'header>)
  (stream: Stream<'id, 'event, 'header>)
  =
  rehydrateEvents zero evolve stream.Events

let applyEvents<'id, 'state, 'event, 'header, 'sideEffect>
  (aggregate: Aggregate<'id, 'state, 'event, 'header>)
  (oldState: 'state)
  (oldStreamState: Stream<'id, 'event, 'header>)
  (events: EventEnvelope<'id, 'event, 'header> list)
  =
  let newEvents = events

  if newEvents.Length = 0 then
    failwith "Events are empty, not supported"

  let newState = newEvents |> List.fold aggregate.evolve oldState

  let lastEvent = newEvents |> List.last

  let combinedEvents = (oldStreamState.Events |> Seq.toList) @ newEvents

  let newStream =
    { oldStreamState with
        Events = combinedEvents |> ResizeArray
        Version = lastEvent.Version }

  let newStateChunk =
    { State = newState
      // maybe change to StreamChunk, when snapshots are involved, not all events are loaded
      Stream = newStream
      EventsChunk = newEvents }

  let previousState =
    { State = oldState
      Stream = oldStreamState
      Events = oldStreamState.Events |> Seq.toList }

  let isDeleted = aggregate.shouldDelete newStateChunk previousState

  let result: AppendEventsResult<'id, 'state, 'event, 'header> =
    { NewState = newState
      ShouldDelete = isDeleted
      NewStream = newStream
      NewEvents = newEvents
      PreviousState = oldState
      PreviousStream = oldStreamState
      PreviousEvents = oldStreamState.Events |> Seq.toList }

  result

let appendEvents<'id, 'state, 'event, 'header, 'sideEffect>
  (aggregate: Aggregate<'id, 'state, 'event, 'header>)
  (currentState: Stream<'id, 'event, 'header>)
  (id: 'id, events: ('event * 'header) list)
  =
  let oldState = rehydrate aggregate.zero aggregate.evolve currentState

  let lastEventNumber =
    currentState.Events
    |> Seq.tryLast
    |> Option.map _.Version
    |> Option.defaultValue 0u

  let newEvents =
    events
    |> List.mapi (fun i (evt, header) -> EventEnvelope.Create(id, evt, header, ((uint i) + lastEventNumber + 1u)))

  let result = applyEvents aggregate oldState currentState newEvents
  result

let makeCommandHandler<'id, 'state, 'event, 'header, 'command, 'commandHeader, 'sideEffect>
  (aggregate: Aggregate<'id, 'state, 'event, 'header>)
  (executeCommand: PureDecide<'id, 'state, 'command, 'commandHeader, 'event, 'header, 'sideEffect>)
  (currentStreamState: Stream<'id, 'event, 'header>)
  =
  fun (command: CommandEnvelope<'id, 'command, 'commandHeader>) ->
    result {
      let highestEventNumber =
        match currentStreamState.Events |> Seq.toList with
        | [] -> -1L
        | elements -> elements |> Seq.map (fun x -> int64 x.Version) |> Seq.max
      // let highestEventNumber = currentStreamState.Events |> Seq.map _.Version |> Seq.max
      let lastEventNumber =
        currentStreamState.Events
        |> Seq.sortBy _.Version
        |> Seq.tryLast
        |> Option.map _.Version
        |> Option.defaultValue 0u

      let isNew = lastEventNumber = 0u
      let oldState = rehydrate aggregate.zero aggregate.evolve currentStreamState

      let isExpectedVersion =
        match command.ExpectedVersion, lastEventNumber with
        | Some expectedVersion, _ -> expectedVersion = lastEventNumber
        | None, _ -> true

      do!
        isExpectedVersion
        |> Result.requireTrue (
          sprintf "Expected version do not match. Expected %A but got %A" command.ExpectedVersion lastEventNumber
        )

      let! newEvents, sideEffects = executeCommand oldState command

      let newEvents =
        newEvents
        |> List.mapi (fun i (evt, header) ->
          EventEnvelope.createEventMetadata (
            evt,
            header,
            command,
            ((uint i) + lastEventNumber + 1u),
            command.CorrelationId
          ))

      let newState = newEvents |> List.fold aggregate.evolve oldState


      let oldEvents = currentStreamState.Events |> Seq.toList
      let combinedEvents = oldEvents @ newEvents

      let streamCreatedAt =
        if isNew then
          combinedEvents
          |> List.tryHead
          |> Option.map _.Timestamp
          |> Option.defaultValue currentStreamState.Created
        else
          currentStreamState.Created

      let lastEvent = combinedEvents |> List.last
      let combinedEvents = System.Collections.Generic.List(oldEvents @ newEvents)

      let newStream =
        { currentStreamState with
            Created = streamCreatedAt
            Modified = lastEvent.Timestamp
            Events = combinedEvents
            Version = lastEvent.Version }

      let newStateChunk =
        { State = newState
          Stream = newStream
          EventsChunk = newEvents }

      let previousState =
        { State = oldState
          Stream = currentStreamState
          Events = oldEvents }

      let isDeleted = aggregate.shouldDelete newStateChunk previousState

      let result: CommandResult<'id, 'state, 'event, 'header, 'sideEffect> =
        { NewState = newState
          NewStream = newStream
          NewEvents = newEvents
          PreviousState = oldState
          PreviousStream = currentStreamState
          PreviousEvents = (oldEvents |> Seq.toList)
          ShouldDelete = isDeleted
          SideEffects = sideEffects }

      return result
    }
