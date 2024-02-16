module TinyEventStore

open System
open System.Threading.Tasks
open FsToolkit.ErrorHandling

type EventId =
  | EventId of Guid
  member this.Value =
    match this with
    | EventId id -> id
  member this.value() =
    match this with
    | EventId id -> id

// maybe here use some AspNetCore Request id
type CommandId = CommandId of Guid

type FailureId = FailureId of Guid

type ExpectedStreamVersion =
  | Expected of int
  | Irrelevant

type IEvent =
  interface
  end

type ICommand =
  interface
  end

[<CLIMutable>]
type EventEnvelope<'TId, 'TEvent when 'TEvent :> IEvent> =
  { StreamId: 'TId
    Payload: 'TEvent
    EventId: EventId
    // CommandId that triggered this event
    // CausationId: CausationId
    EventNumber: int
    Timestamp: DateTime
  // Headers
   }

// let envelope v deserialize =
//   { EventId = v.Id |> EventId
//     EventEnvelope.Payload = deserialize v.Data
//     StreamId = v.StreamId
//     EventNumber = v.Version
//     Timestamp = v.Timestamp
//   }

// let envelopeEvents<'TId, 'Event when 'Event :> IEvent> events deserialize =
//   let result: Result<EventEnvelope<'TId, 'Event> list, string> =
//     events
//     |> List.map (fun v -> envelope v deserialize)
//     |> Result.Ok
//
//   result

type CommandEnvelope<'TId, 'TCommand when 'TCommand :> ICommand> =
  { StreamId: 'TId
    Payload: 'TCommand
    CommandId: CommandId
    ExpectedVersion: ExpectedStreamVersion
    Timestamp: DateTime }

type Aggregate<'TId, 'TState, 'TCommand, 'TEvent when 'TEvent :> IEvent> =
  { Zero: 'TState
    ApplyEvent: 'TState -> EventEnvelope<'TId, 'TEvent> -> 'TState
    ExecuteCommand: 'TState -> 'TCommand -> Task<Result<'TEvent list, string>> }

// type ProcessManager<'TState> =
//   { Zero: 'TState
//     ApplyEvent: 'TState -> EventEnvelope<IEvent> -> 'TState
//     ProcessEvent: 'TState -> EventEnvelope<IEvent> -> Result<(QueueName * CommandEnvelope<ICommand>) list, IError> }

// Events
// let createEvent aggregateId (causationId, processId, correlationId) payload timestamp =
//   { AggregateId = aggregateId
//     Payload = payload
//     EventId = Guid.NewGuid() |> EventId
//     ProcessId = processId
//     CausationId = causationId
//     CorrelationId = correlationId
//     EventNumber = 0
//     Timestamp = timestamp }

let createEventMetadata payload command eventNumber =
  let (CommandId cmdGuid) = command.CommandId

  { StreamId = command.StreamId
    Payload = payload
    EventId = Guid.NewGuid() |> EventId
    // ProcessId = command.ProcessId
    // CausationId = CausationId cmdGuid
    // CorrelationId = command.CorrelationId
    EventNumber = eventNumber
    Timestamp = command.Timestamp }

// Commands

// let createCommand aggregateId (version, causationId, correlationId, processId) payload timestamp =
//   let commandId = Guid.NewGuid()
//
//   let causationId' =
//     match causationId with
//     | Some c -> c
//     | _ -> CausationId commandId
//
//   let correlationId' =
//     match correlationId with
//     | Some c -> c
//     | _ -> CorrelationId commandId
//
//   { AggregateId = aggregateId
//     Payload = payload
//     CommandId = CommandId commandId
//     ProcessId = processId
//     CausationId = causationId'
//     CorrelationId = correlationId'
//     ExpectedVersion = version
//     Timestamp = timestamp }

type InlineEventHandler<'TId, 'TState, 'TEvent when 'TEvent :> IEvent> = 'TState * EventEnvelope<'TId, 'TEvent> list -> unit

let makeCommandHandler
  (aggregate: Aggregate<'TId, 'TState, 'TCommand, 'TEvent>)
  (load: 'TId -> TaskResult<EventEnvelope<'TId, 'TEvent> list, string>)
  (commit: EventEnvelope<'TId, 'TEvent> list -> Result<unit, string>)
  (handlers: InlineEventHandler<'TId, 'TState, 'TEvent> list)
  =
  let applyCommand command events =
    taskResult {
      let lastEventNumber = List.fold (fun _ e' -> e'.EventNumber) 0 events

      do!
        (command.ExpectedVersion = Irrelevant
         || command.ExpectedVersion = Expected lastEventNumber)
        |> Result.requireTrue "Expected version miss match"

      let state =
        events
        |> List.fold aggregate.ApplyEvent aggregate.Zero

      let! newEvents = aggregate.ExecuteCommand state command.Payload

      let newEvents =
        newEvents
        |> List.mapi (fun i e -> createEventMetadata e command (i + lastEventNumber + 1))
      // TODO: if successful

      let newState = newEvents |> List.fold aggregate.ApplyEvent state

      // committing should maybe not happen here, as we might want to coordinate an outer transaction
      printfn "Committing events"
      let! commitResult = commit newEvents
      printfn "Result %A" commitResult
      return newState, newEvents
    }

  fun command ->
    taskResult {
      let id = command.StreamId
      let! loadedEvents = load id |> TaskResult.map(fun events -> events |> List.sortBy(fun x -> x.Timestamp))
      let! newState, newEvents = applyCommand command loadedEvents

      handlers
      |> List.iter (fun handler -> handler (newState, newEvents))

      return ()
    }
