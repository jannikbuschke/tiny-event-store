namespace TinyEventStore

open System
open System.Collections.Generic
open System.Threading.Tasks
open FsToolkit.ErrorHandling

type Version = uint

/// <summary> A container for Events </summary>
[<CLIMutable>]
type Stream<'id, 'event, 'header> =
  { Id: 'id
    Version: Version
    IsDeleted: bool
    Created: DateTimeOffset
    Modified: DateTimeOffset
    Events: EventEnvelope<'id, 'event, 'header> ICollection }

/// <summary>
/// summary: Wraps a single Event. This is meant to be serialized to some storage.
/// </summary>
and [<CLIMutable>] EventEnvelope<'streamId, 'event, 'header> =
  { SequenceId: uint32
    StreamId: 'streamId
    Payload: 'event
    IsDeleted: bool
    EventId: EventId
    CausationId: CausationId option
    // TODO: this could be a string to allow for more flexibility
    CorrelationId: CorrelationId option
    Version: Version
    Timestamp: DateTimeOffset
    Header: 'header }

  member this.IsInitialEvent() = this.Version = 1u

  static member Create(streamId: 'streamId, payload: 'event, header: 'header, eventNumber: Version) =
    { SequenceId = 0ul
      StreamId = streamId
      IsDeleted = false
      Payload = payload
      EventId = EventId.New()
      CausationId = None
      CorrelationId = None
      Version = eventNumber
      Timestamp = DateTimeOffset.UtcNow
      Header = header }

  static member createEventMetadata
    (payload, header, command: CommandEnvelope<'streamId, 'command, 'commandHeader>, eventNumber, correlationId) : EventEnvelope<
                                                                                                                     'streamId,
                                                                                                                     'event,
                                                                                                                     'header
                                                                                                                    >
    =
    { SequenceId = 0ul
      StreamId = command.StreamId
      Payload = payload
      IsDeleted = false
      EventId = EventId.New()
      CausationId = Some(CausationId.CommandId command.CommandId)
      CorrelationId = correlationId
      Version = eventNumber
      Timestamp = command.Timestamp
      Header = header }

and CommandEnvelope<'TId, 'TCommand, 'commandHeader> =
  { StreamId: 'TId
    Header: 'commandHeader
    Payload: 'TCommand
    CorrelationId: CorrelationId option
    CausationId: EventId option
    CommandId: CommandId
    ExpectedVersion: uint option
    Timestamp: DateTimeOffset }

module CommandEnvelope =
  let createCommandEnvelope streamId payload header timestamp version correlationId causationId =
    { StreamId = streamId
      Payload = payload
      Header = header
      CausationId = causationId
      CommandId = CommandId.New()
      ExpectedVersion = version
      Timestamp = timestamp
      CorrelationId = correlationId }

[<AbstractClass; Sealed>]
type CommandEnvelope() =
  static member New(streamId, payload, header) =
    CommandEnvelope.createCommandEnvelope streamId payload header DateTimeOffset.UtcNow None None None

  static member New(streamId, payload, header, version) =
    CommandEnvelope.createCommandEnvelope streamId payload header DateTimeOffset.UtcNow version None None

  static member New(streamId, payload, header, timestamp) =
    CommandEnvelope.createCommandEnvelope streamId payload header timestamp None None None

  static member New(streamId, payload, header, timestamp, version) =
    CommandEnvelope.createCommandEnvelope streamId payload header timestamp version None None

  static member New(streamId, payload, header, correlationId) =
    CommandEnvelope.createCommandEnvelope streamId payload header DateTimeOffset.Now None (Some correlationId) None

  static member New(streamId, payload, header, timestamp, version, correlationId, causationId) =
    CommandEnvelope.createCommandEnvelope
      streamId
      payload
      header
      timestamp
      version
      (Some correlationId)
      (Some causationId)

type StateChunk<'id, 'state, 'event, 'header> =
  { State: 'state
    Stream: Stream<'id, 'event, 'header>
    EventsChunk: EventEnvelope<'id, 'event, 'header> list }

  member this.IsNew() =
    (this.EventsChunk.Item 0).IsInitialEvent()

type StateHead<'id, 'state, 'event, 'header> =
  { State: 'state
    Stream: Stream<'id, 'event, 'header>
    Events: EventEnvelope<'id, 'event, 'header> list }

type OperationResult<'id, 'state, 'event, 'header> =
  abstract New: StateChunk<'id, 'state, 'event, 'header>
  abstract Previous: StateHead<'id, 'state, 'event, 'header>
  abstract ShouldDelete: bool
  abstract IsDeleted: bool
  abstract NewState: 'state
  abstract NewStream: Stream<'id, 'event, 'header>
  abstract NewEvents: EventEnvelope<'id, 'event, 'header> list
  abstract PreviousState: 'state
  abstract PreviousStream: Stream<'id, 'event, 'header>
  abstract PreviousEvents: EventEnvelope<'id, 'event, 'header> list

type AppendEventsResult<'id, 'state, 'event, 'header> =
  { NewState: 'state
    NewStream: Stream<'id, 'event, 'header>
    IsDeleted: bool
    NewEvents: EventEnvelope<'id, 'event, 'header> list
    PreviousState: 'state
    PreviousStream: Stream<'id, 'event, 'header>
    PreviousEvents: EventEnvelope<'id, 'event, 'header> list
    ShouldDelete: bool }

  member this.IsNew() =
    (this.NewEvents.Item 0).IsInitialEvent()

  interface OperationResult<'id, 'state, 'event, 'header> with
    member this.New =
      { State = this.NewState
        Stream = this.NewStream
        EventsChunk = this.NewEvents }

    member this.Previous =
      { State = this.NewState
        Stream = this.NewStream
        Events = this.NewEvents }

    member this.ShouldDelete = this.ShouldDelete
    member this.IsDeleted = this.IsDeleted
    member this.NewEvents = this.NewEvents
    member this.NewState = this.NewState
    member this.NewStream = this.NewStream
    member this.PreviousEvents = this.PreviousEvents
    member this.PreviousState = this.PreviousState
    member this.PreviousStream = this.NewStream

type CommandResult<'id, 'state, 'event, 'header, 'sideEffect> =
  { NewState: 'state
    NewStream: Stream<'id, 'event, 'header>
    NewEvents: EventEnvelope<'id, 'event, 'header> list
    SideEffects: 'sideEffect list
    PreviousState: 'state
    PreviousStream: Stream<'id, 'event, 'header>
    PreviousEvents: EventEnvelope<'id, 'event, 'header> list
    IsDeleted: bool
    ShouldDelete: bool }

  interface OperationResult<'id, 'state, 'event, 'header> with
    member this.New =
      { State = this.NewState
        Stream = this.NewStream
        EventsChunk = this.NewEvents }

    member this.Previous =
      { State = this.NewState
        Stream = this.NewStream
        Events = this.NewEvents }

    member this.NewEvents = this.NewEvents
    member this.NewState = this.NewState
    member this.NewStream = this.NewStream
    member this.PreviousEvents = this.PreviousEvents
    member this.PreviousState = this.PreviousState
    member this.PreviousStream = this.NewStream
    member this.ShouldDelete = this.ShouldDelete
    member this.IsDeleted = this.IsDeleted

type Decision<'event, 'header, 'sideEffect> = Result<('event * 'header) list * 'sideEffect list, string>

type PureDecide<'id, 'state, 'command, 'cHeader, 'event, 'header, 'sideEffect> =
  'state -> CommandEnvelope<'id, 'command, 'cHeader> -> Decision<'event, 'header, 'sideEffect>

type Decide<'state, 'command, 'event, 'header, 'sideEffect> =
  'state -> 'command -> Task<Result<('event * 'header) list * 'sideEffect list, string>>

type Evolve<'id, 'state, 'event, 'header> = 'state -> EventEnvelope<'id, 'event, 'header> -> 'state

type LoadStreamContainer<'id, 'event, 'header> = 'id -> TaskResult<Stream<'id, 'event, 'header>, string>

type RehydrateFn<'id, 'state, 'event, 'header> =
  'state -> Evolve<'id, 'state, 'event, 'header> -> Stream<'id, 'event, 'header> -> 'state

type ImpureStore<'id, 'state, 'command, 'event, 'header, 'sideEffect> =
  { decide: Decide<'state, 'command, 'event, 'header, 'sideEffect>
    append: EventEnvelope<'id, 'event, 'header> -> TaskResult<unit, string>
    rehydrate: RehydrateFn<'id, 'state, 'event, 'header> }

type Aggregate<'id, 'state, 'event, 'header> =
  { zero: 'state
    evolve: Evolve<'id, 'state, 'event, 'header>
    shouldDelete: StateChunk<'id, 'state, 'event, 'header> -> StateHead<'id, 'state, 'event, 'header> -> bool }
