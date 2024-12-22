module TinyEventStore.ApplyEvents

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

open System.Linq

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
        Modified = if combinedEvents.Length > 0 then combinedEvents.Last().Timestamp else oldStreamState.Modified
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

  let shouldDelete = aggregate.shouldDelete newStateChunk previousState

  let result: AppendEventsResult<'id, 'state, 'event, 'header> =
    { NewState = newState
      IsDeleted = oldStreamState.IsDeleted
      ShouldDelete = shouldDelete
      NewStream = newStream
      NewEvents = newEvents
      PreviousState = oldState
      PreviousStream = oldStreamState
      PreviousEvents = oldStreamState.Events |> Seq.toList }

  result
