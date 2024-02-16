module TinyEventStore

open System
open System.Threading.Tasks
open FsToolkit.ErrorHandling

type EventId =
  | EventId of Guid
  member this.Value =
    match this with
    | EventId id -> id

type CommandId = CommandId of Guid

type FailureId = FailureId of Guid

type ExpectedStreamVersion =
  | Expected of int
  | Irrelevant

// type IEvent =
//   interface
//   end

// type ICommand =
//   interface
//   end

[<CLIMutable>]
type EventEnvelope<'TId, 'event, 'header> =
  { StreamId: 'TId
    Payload: 'event
    EventId: EventId
    // CommandId that triggered this event
    // CausationId: CausationId
    EventNumber: int
    Timestamp: DateTime
    Header: 'header }

type CommandEnvelope<'TId, 'TCommand> =
  { StreamId: 'TId
    Payload: 'TCommand
    CommandId: CommandId
    ExpectedVersion: ExpectedStreamVersion
    Timestamp: DateTime }

let createEventMetadata payload header command eventNumber =
  let (CommandId cmdGuid) = command.CommandId

  { StreamId = command.StreamId
    Payload = payload
    EventId = Guid.NewGuid() |> EventId
    EventNumber = eventNumber
    Timestamp = command.Timestamp
    Header = header }

type InlineEventHandler<'id, 'state, 'event, 'header> = 'state * EventEnvelope<'id, 'event, 'header> list -> unit

type Aggregate<'id, 'state, 'command, 'event, 'header> =
  { Zero: 'state
    ApplyEvent: 'state -> EventEnvelope<'id, 'event, 'header> -> 'state
    ExecuteCommand: 'state -> 'command -> Task<Result<'event list, string>> }

type CommandResult<'event, 'header, 'sideEffect> = Task<Result<('event * 'header) list * 'sideEffect list, string>>

let makeCommandHandler
  (zero: 'state)
  (applyEvent: 'state -> EventEnvelope<'id, 'event, 'header> -> 'state)
  (executeCommand: 'state -> 'command -> CommandResult<'event, 'header, 'sideEffect>)
  (load: 'id -> TaskResult<EventEnvelope<'id, 'event, 'header> list, string>)
  (commit: EventEnvelope<'id, 'event, 'header> list * 'sideEffect list -> Result<unit, string>)
  (handlers: InlineEventHandler<'id, 'state, 'event, 'header> list)
  =
  let applyCommand command events =
    taskResult {
      let lastEventNumber = List.fold (fun _ e' -> e'.EventNumber) 0 events

      do!
        (command.ExpectedVersion = Irrelevant
         || command.ExpectedVersion = Expected lastEventNumber)
        |> Result.requireTrue (sprintf "Expected version do not match. Expected %A but got %A" command.ExpectedVersion (Expected lastEventNumber))

      let state = events |> List.fold applyEvent zero

      let! (newEvents,sideEffects) = executeCommand state command.Payload

      let newEvents =
        newEvents
        |> List.mapi (fun i (evt, header) -> createEventMetadata evt header command (i + lastEventNumber + 1))

      let newState = newEvents |> List.fold applyEvent state

      let! commitResult = commit (newEvents, sideEffects)
      return newState, newEvents
    }

  fun command ->
    taskResult {
      let id = command.StreamId

      let! loadedEvents =
        load id
        |> TaskResult.map (fun events -> events |> List.sortBy (fun x -> x.Timestamp))

      let! newState, newEvents = applyCommand command loadedEvents

      handlers
      |> List.iter (fun handler -> handler (newState, newEvents))

      return ()
    }
