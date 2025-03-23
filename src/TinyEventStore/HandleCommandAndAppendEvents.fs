module TinyEventStore.HandleCommandAndEvents

open TinyEventStore.ApplyEvents
open FsToolkit.ErrorHandling
open TinyEventStore

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
      // let highestEventNumber =
      //   match currentStreamState.Events |> Seq.toList with
      //   | [] -> -1L
      //   | elements -> elements |> Seq.map (fun x -> int64 x.Version) |> Seq.max
      // let highestEventNumber = currentStreamState.Events |> Seq.map _.Version |> Seq.max
      let lastEventNumber =
        currentStreamState.Events
        |> Seq.sortBy _.Version
        |> Seq.tryLast
        |> Option.map _.Version
        |> Option.defaultValue 0u

      // let isNew = lastEventNumber = 0u
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

      let result = applyEvents aggregate oldState currentStreamState newEvents

      let result: CommandResult<'id, 'state, 'event, 'header, 'sideEffect> =
        { NewState = result.NewState
          NewStream = result.NewStream
          NewEvents = result.NewEvents
          PreviousState = result.PreviousState
          PreviousStream = result.PreviousStream
          PreviousEvents = result.PreviousEvents
          IsDeleted = result.IsDeleted
          ShouldDelete = result.ShouldDelete
          SideEffects = sideEffects }

      return result
    // let newState = newEvents |> List.fold aggregate.evolve oldState
    //
    //
    // let oldEvents = currentStreamState.Events |> Seq.toList
    // let combinedEvents = oldEvents @ newEvents
    //
    // let streamCreatedAt =
    //   if isNew then
    //     combinedEvents
    //     |> List.tryHead
    //     |> Option.map _.Timestamp
    //     |> Option.defaultValue currentStreamState.Created
    //   else
    //     currentStreamState.Created
    //
    // let lastEvent = combinedEvents |> List.last
    // let combinedEvents = System.Collections.Generic.List(oldEvents @ newEvents)
    //
    // let newStream =
    //   { currentStreamState with
    //       Created = streamCreatedAt
    //       Modified = lastEvent.Timestamp
    //       Events = combinedEvents
    //       Version = lastEvent.Version }
    //
    // let newStateChunk =
    //   { State = newState
    //     Stream = newStream
    //     EventsChunk = newEvents }
    //
    // let previousState =
    //   { State = oldState
    //     Stream = currentStreamState
    //     Events = oldEvents }
    //
    // let isDeleted = aggregate.shouldDelete newStateChunk previousState
    //
    // let result: CommandResult<'id, 'state, 'event, 'header, 'sideEffect> =
    //   { NewState = newState
    //     NewStream = newStream
    //     NewEvents = newEvents
    //     PreviousState = oldState
    //     PreviousStream = currentStreamState
    //     PreviousEvents = (oldEvents |> Seq.toList)
    //     IsDeleted = currentStreamState.IsDeleted
    //     ShouldDelete = isDeleted
    //     SideEffects = sideEffects }
    //
    // return result
    }
