module TinyEventStore.Store

open FsToolkit.ErrorHandling
open TinyEventStore

let projector<'id, 'state, 'event, 'header, 'sideEffect>
  (create: EventEnvelope<'id, 'event, 'header> -> 'state)
  (evolve: Evolve<'id, 'state, 'event, 'header>)
  (load: LoadStreamContainer<'id, 'event, 'header>)
  (snapshot: ('state * Version * System.DateTimeOffset) option)
  (id: 'id)
  =
  taskResult {
    // instead of loading all here, we might want to load only a slice of events. Event range could be determined by event number or event timestamp
    let! oldStream = load id

    let zero, events =
      match snapshot with
      | Some(snapshot, snapshotVersion, _) ->
        let events = oldStream.Events |> Seq.where (fun x -> x.Version > snapshotVersion)

        snapshot, events
      | None ->
        let events = oldStream.Events |> Seq.sortBy _.Version |> Seq.toList

        match events with
        | head :: tail ->
          let zero = create head
          zero, tail
        | [] -> failwith "not events in stream found"

    let currentState = events |> Seq.toList |> List.fold evolve zero

    return currentState, events |> Seq.last
  }

let appendEvents<'id, 'state, 'event, 'header, 'sideEffect>
  (zero: 'state)
  (evolve: Evolve<'id, 'state, 'event, 'header>)
  (load: LoadStreamContainer<'id, 'event, 'header>)
  : 'id * ('event * 'header) list -> TaskResult<AppendEventsResult<'id, 'state, 'event, 'header>, string> =
  fun (id, events) ->
    taskResult {
      let! oldStream = load id

      let oldEvents = oldStream.Events |> Seq.sortBy _.Version |> Seq.toList

      let lastEventNumber = List.fold (fun _ e' -> e'.Version) 0u oldEvents

      let oldState = oldEvents |> List.fold evolve zero

      let newEvents = events

      let newEvents =
        newEvents
        |> List.mapi (fun i (evt, header) -> EventEnvelope.Create(id, evt, header, ((uint i) + lastEventNumber + 1u)))

      let newState = newEvents |> List.fold evolve oldState

      let lastEvent = newEvents |> List.last

      let combinedEvents = System.Collections.Generic.List(oldEvents @ newEvents)

      let newStream =
        { oldStream with
            Events = combinedEvents
            Version = lastEvent.Version }

      let result: AppendEventsResult<'id, 'state, 'event, 'header> =
        { NewState = newState
          NewStream = newStream
          NewEvents = newEvents
          PreviousState = oldState
          PreviousStream = oldStream
          PreviousEvents = oldEvents }

      return result
    }



let makeCommandHandler<'id, 'state, 'event, 'header, 'command, 'sideEffect>
  (zero: 'state)
  (evolve: Evolve<'id, 'state, 'event, 'header>)
  (executeCommand: Decide<'state, 'command, 'event, 'header, 'sideEffect>)
  (load: LoadStreamContainer<'id, 'event, 'header>)
  : CommandEnvelope<'id, 'command> -> TaskResult<CommandResult<'id, 'state, 'event, 'header, 'sideEffect>, string> =
  fun (command: CommandEnvelope<'id, 'command>) ->
    taskResult {
      let! oldStream = load command.StreamId

      let oldEvents = oldStream.Events |> Seq.sortBy _.Version |> Seq.toList

      let lastEventNumber = List.fold (fun _ e' -> e'.Version) 0u oldEvents

      let isExpectedVersion =
        match command.ExpectedVersion, lastEventNumber with
        | Some expectedVersion, _ -> expectedVersion = lastEventNumber
        | None, _ -> true

      do!
        isExpectedVersion
        |> Result.requireTrue (
          sprintf "Expected version do not match. Expected %A but got %A" command.ExpectedVersion lastEventNumber
        )

      let oldState = oldEvents |> List.fold evolve zero

      let! newEvents, sideEffects = executeCommand oldState command.Payload

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

      let newState = newEvents |> List.fold evolve oldState

      let lastEvent = newEvents |> List.last

      let combinedEvents = System.Collections.Generic.List(oldEvents @ newEvents)

      let newStream =
        { oldStream with
            Events = combinedEvents
            Version = lastEvent.Version }

      let result: CommandResult<'id, 'state, 'event, 'header, 'sideEffect> =
        { NewState = newState
          NewStream = newStream
          NewEvents = newEvents
          SideEffects = sideEffects
          PreviousState = oldState
          PreviousStream = oldStream
          PreviousEvents = oldEvents }

      return result
    }

let create<'id, 'state, 'event, 'header, 'command, 'sideEffect>
  (zero: 'state)
  (evolve: Evolve<'id, 'state, 'event, 'header>)
  (load: LoadStreamContainer<'id, 'event, 'header>)
  (executeCommand: Decide<'state, 'command, 'event, 'header, 'sideEffect>)
  =
  let commandHandler =
    makeCommandHandler<'id, 'state, 'event, 'header, 'command, 'sideEffect> zero evolve executeCommand load

  (commandHandler, appendEvents<'id, 'state, 'event, 'header, 'sideEffect> zero evolve load)
