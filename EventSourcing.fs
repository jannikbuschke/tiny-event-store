module ES

open System
open FsToolkit.ErrorHandling

type EventId =
  | EventId of Guid

  member this.value() =
    match this with
    | EventId id -> id

type CommandId = CommandId of Guid
type FailureId = FailureId of Guid

type AggregateId =
  | AggregateId of Guid

  member this.value() =
    match this with
    | AggregateId id -> id

type CausationId = CausationId of Guid
type CorrelationId = CorrelationId of Guid
type ProcessId = ProcessId of Guid
type QueueName = QueueName of string
type Category = Category of string

type AggregateVersion =
  | Expected of int
  | Irrelevant

type EventNumber = int

type IEvent =
  interface
  end

type PersistedEvent =
  { Id: Guid
    StreamId: Guid
    Version: int
    Data: string
    Timestamp: DateTime
    Archived: bool }

  interface IEvent

type ICommand =
  interface
  end

type IError =
  interface
  end

type Failure(_x: string) =
  override this.ToString() = _x
  interface IError

// Aggregates

type EventEnvelope<'TEvent when 'TEvent :> IEvent> =
  { AggregateId: AggregateId
    Payload: 'TEvent
    EventId: EventId
    //
    ProcessId: ProcessId option
    // CommandId that triggered this event
    CausationId: CausationId
    CorrelationId: CorrelationId
    EventNumber: EventNumber
    Timestamp: DateTime }

type CommandEnvelope<'TCommand when 'TCommand :> ICommand> =
  { AggregateId: AggregateId
    Payload: 'TCommand
    CommandId: CommandId
    ProcessId: ProcessId option
    CausationId: CausationId
    CorrelationId: CorrelationId
    ExpectedVersion: AggregateVersion
    Timestamp: DateTime }

type Aggregate<'TState, 'TCommand, 'TEvent when 'TEvent :> IEvent> =
  { Zero: 'TState
    ApplyEvent: 'TState -> EventEnvelope<'TEvent> -> 'TState
    ExecuteCommand: 'TState -> 'TCommand -> Result<'TEvent list, IError> }

type ProcessManager<'TState> =
  { Zero: 'TState
    ApplyEvent: 'TState -> EventEnvelope<IEvent> -> 'TState
    ProcessEvent: 'TState -> EventEnvelope<IEvent> -> Result<(QueueName * CommandEnvelope<ICommand>) list, IError> }

// Events
let createEvent aggregateId (causationId, processId, correlationId) payload timestamp =
  { AggregateId = aggregateId
    Payload = payload
    EventId = Guid.NewGuid() |> EventId
    ProcessId = processId
    CausationId = causationId
    CorrelationId = correlationId
    EventNumber = 0
    Timestamp = timestamp }

let createEventMetadata payload command eventNumber =
  let (CommandId cmdGuid) = command.CommandId

  { AggregateId = command.AggregateId
    Payload = payload
    EventId = Guid.NewGuid() |> EventId
    ProcessId = command.ProcessId
    CausationId = CausationId cmdGuid
    CorrelationId = command.CorrelationId
    EventNumber = eventNumber
    Timestamp = command.Timestamp }

let makeEventProcessor
  (processManager: ProcessManager<'TState>)
  (load: ProcessId -> Result<EventEnvelope<IEvent> list, IError>)
  (enqueue: (QueueName * CommandEnvelope<ICommand>) list -> Result<CommandEnvelope<ICommand> list, IError>)
  =
  let handleEvent (event: EventEnvelope<IEvent>) : Result<CommandEnvelope<ICommand> list, IError> =
    result {
      let processEvents events =
        result {
          let state = List.fold processManager.ApplyEvent processManager.Zero events

          let! result = processManager.ProcessEvent state event
          return enqueue result
        }

      let! pid =
        event.ProcessId
        |> Result.requireSome ((Failure "No process id on event") :> IError)

      let! loadedEvents = load pid
      let! result = processEvents loadedEvents
      return! result
    }

  handleEvent

// Commands

let createCommand aggregateId (version, causationId, correlationId, processId) payload timestamp =
  let commandId = Guid.NewGuid()

  let causationId' =
    match causationId with
    | Some c -> c
    | _ -> CausationId commandId

  let correlationId' =
    match correlationId with
    | Some c -> c
    | _ -> CorrelationId commandId

  { AggregateId = aggregateId
    Payload = payload
    CommandId = CommandId commandId
    ProcessId = processId
    CausationId = causationId'
    CorrelationId = correlationId'
    ExpectedVersion = version
    Timestamp = timestamp }

type InlineEventHandler<'TState, 'TEvent when 'TEvent :> IEvent> = 'TState * EventEnvelope<'TEvent> list -> unit

let makeCommandHandler
  (aggregate: Aggregate<'TState, 'TCommand, 'TEvent>)
  (load: AggregateId -> Result<EventEnvelope<'TEvent> list, IError>)
  (commit: EventEnvelope<'TEvent> list -> Result<unit, IError>)
  (handlers: InlineEventHandler<'TState, 'TEvent> list)
  // maybe add handlers here?
  =
  let handleCommand command : Result<unit, IError> =
    result {
      let processEvents events =

        result {
          let lastEventNumber = List.fold (fun _ e' -> e'.EventNumber) 0 events

          do!
            (command.ExpectedVersion = Irrelevant
             || command.ExpectedVersion = Expected lastEventNumber)
            |> Result.requireTrue ((Failure "Expected version miss match") :> IError)

          let state = events |> List.fold aggregate.ApplyEvent aggregate.Zero

          let! newEvents = aggregate.ExecuteCommand state command.Payload

          let newEvents =
            newEvents
            |> List.mapi (fun i e -> createEventMetadata e command (i + lastEventNumber + 1))
          // TODO: if successful

          let newState = newEvents |> List.fold aggregate.ApplyEvent state

          printfn "Committing events"
          let! commitResult = commit newEvents
          printfn "Result %A" commitResult
          return newState, newEvents

        }

      let id = command.AggregateId
      let! loadedEvents = load id
      let! newState, newEvents = processEvents loadedEvents

      handlers |> List.iter (fun handler -> handler (newState, newEvents))

      return ()
    }

  handleCommand
