module TinyEventStore.PureStore

open FsToolkit.ErrorHandling
open TinyEventStore

let rehydrate<'id, 'state, 'event, 'header>
  (zero: 'state)
  (evolve: Evolve<'id, 'state, 'event, 'header>)
  (stream: Stream<'id, 'event, 'header>)
  =
  let evolveWithVersionCheck (state, version) e =
    if e.Version <= version then
      failwith "Events are not ordered"

    let innerState = evolve state e
    (innerState, e.Version)

  let state, _ =
    stream.Events
    |> Seq.sortBy _.Version
    |> Seq.fold evolveWithVersionCheck (zero, 0u)

  state

let appendEvents<'id, 'state, 'event, 'header, 'sideEffect>
  (zero: 'state)
  (evolve: Evolve<'id, 'state, 'event, 'header>)
  (currenState: Stream<'id, 'event, 'header>)
  =
  fun (id: 'id, events: ('event * 'header) list) ->
    let lastEventNumber =
      currenState.Events
      |> Seq.tryLast
      |> Option.map _.Version
      |> Option.defaultValue 0u

    let oldState = rehydrate zero evolve currenState

    let newEvents = events

    let newEvents =
      newEvents
      |> List.mapi (fun i (evt, header) -> EventEnvelope.Create(id, evt, header, ((uint i) + lastEventNumber + 1u)))

    let newState = newEvents |> List.fold evolve oldState

    let lastEvent = newEvents |> List.last

    let combinedEvents = (currenState.Events |> Seq.toList)  @ newEvents

    let newStream =
      { currenState with
          Events = combinedEvents |> ResizeArray
          Version = lastEvent.Version }

    let result: AppendEventsResult<'id, 'state, 'event, 'header> =
      { NewState = newState
        NewStream = newStream
        NewEvents = newEvents
        PreviousState = oldState
        PreviousStream = currenState
        PreviousEvents = currenState.Events |> Seq.toList }

    result

let makeCommandHandler<'id, 'state, 'event, 'header, 'command,'commandHeader, 'sideEffect>
  (zero: 'state)
  (evolve: Evolve<'id, 'state, 'event, 'header>)
  (executeCommand: PureDecide<'id, 'state, 'command,'commandHeader, 'event, 'header, 'sideEffect>)
  (currentStreamState: Stream<'id, 'event, 'header>)
  =
  fun (command: CommandEnvelope<'id, 'command,'commandHeader>) ->
    result {
      printfn "events %A" (currentStreamState.Events |> Seq.toList)
      let highestEventNumber =
        match currentStreamState.Events|>Seq.toList with
        | [] -> -1L
        | elements -> elements |> Seq.map(fun x -> int64 x.Version) |> Seq.max
      // let highestEventNumber = currentStreamState.Events |> Seq.map _.Version |> Seq.max
      printfn "highest event number %A" highestEventNumber
      let lastEventNumber =
        currentStreamState.Events
        |> Seq.sortBy _.Version
        |> Seq.tryLast
        |> Option.map _.Version
        |> Option.defaultValue 0u
      printfn "last event number %A" lastEventNumber

      let oldState = rehydrate zero evolve currentStreamState

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

      let newState = newEvents |> List.fold evolve oldState


      let oldEvents = currentStreamState.Events |> Seq.toList
      let combinedEvents = oldEvents @ newEvents
      let lastEvent = combinedEvents |> List.last
      let combinedEvents = System.Collections.Generic.List(oldEvents @ newEvents)

      let newStream =
        { currentStreamState with
            Events = combinedEvents// |> Seq.toList
            Version = lastEvent.Version }

      let result: CommandResult<'id, 'state, 'event, 'header, 'sideEffect> =
        { NewState = newState
          NewStream = newStream
          NewEvents = newEvents
          PreviousState = oldState
          PreviousStream = currentStreamState
          PreviousEvents = (oldEvents |> Seq.toList)
          SideEffects = sideEffects }

      return result
    }

let create<'id, 'state, 'event, 'header, 'command, 'commandHeader,'sideEffect>
  (zero: 'state)
  (evolve: Evolve<'id, 'state, 'event, 'header>)
  (executeCommand: PureDecide<'id, 'state, 'command, 'commandHeader, 'event, 'header, 'sideEffect>)
  =
  let commandHandler =
    makeCommandHandler<'id, 'state, 'event, 'header, 'command, 'commandHEader, 'sideEffect> zero evolve executeCommand

  let appendEventsHandler =
    appendEvents<'id, 'state, 'event, 'header, 'sideEffect> zero evolve

  let rehydrate = rehydrate<'id, 'state, 'event, 'header> zero evolve
  (commandHandler, appendEventsHandler, rehydrate)
