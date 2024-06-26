module TinyEventStore.Ef.Storables

open System
open System.Collections.Generic
open TinyEventStore

[<AbstractClass>]
type AbstractStorableStream<'id when 'id: equality>() =
  abstract member Id: 'id with get, set
  member val Version = Unchecked.defaultof<uint32> with get, set
  member val Created = Unchecked.defaultof<DateTimeOffset> with get, set
  member val Modified = Unchecked.defaultof<DateTimeOffset> with get, set

  member this.HasValidVersion() = this.Version > 0u

  member this.IsValid() =
    this.HasValidVersion()
    && (this.Id <> Unchecked.defaultof<'id>)

and StorableStream<'id, 'event, 'header when 'id: equality>() =
  inherit AbstractStorableStream<'id>()
  let mutable id = Unchecked.defaultof<'id>
  override this.Id = id

  override this.Id
    with set value = id <- value

  member val Children = Unchecked.defaultof<ICollection<StorableEvent<'id, 'event, 'header>>> with get, set

and CausationType =
  | Command = 1uy
  | Event = 2uy

and [<AllowNullLiteral>] StorableCausationId() =
  member val Type = Unchecked.defaultof<CausationType> with get, set
  member val Id = Unchecked.defaultof<Guid> with get, set

and [<AbstractClass>] AbstractStorableEvent<'id>() =
  member val SequenceId = Unchecked.defaultof<uint32> with get, set
  member val EventId = Unchecked.defaultof<EventId> with get, set
  member val Version = Unchecked.defaultof<uint32> with get, set
  member val Timestamp = Unchecked.defaultof<DateTimeOffset> with get, set
  member val CausationId = Unchecked.defaultof<StorableCausationId option> with get, set
  member val CorrelationId = Unchecked.defaultof<CorrelationId option> with get, set
  member this.HasValidSequenceId() = this.SequenceId > 0ul
  member this.HasValidVersion() = this.Version > 0u

  member this.IsValid() =
    this.HasValidSequenceId()
    && this.HasValidVersion()

  member this.InvalidReason() =
    if this.IsValid() then
      None
    else
      let versionIsValid = this.HasValidVersion()
      let sequenceIsValid = this.HasValidSequenceId()
      Some $"Invalid event. Version is valid = {versionIsValid}, Sequence is valid = {sequenceIsValid}"

and StorableEvent<'id, 'event, 'header when 'id: equality>() =
  inherit AbstractStorableEvent<'id>()
  member val Data = Unchecked.defaultof<'event> with get, set
  member val Header = Unchecked.defaultof<'header> with get, set
  member val StreamId = Unchecked.defaultof<'id> with get, set
  member val Stream = Unchecked.defaultof<StorableStream<'id, 'event, 'header>> with get, set

type StreamChunk<'id, 'event, 'header when 'id: equality> =
  { StreamId: 'id
    FromSequenceId: uint32
    ToSequenceId: uint32
    Events: StorableEvent<'id, 'event, 'header> list
    // StreamChunk: StorableStream<'id, 'event, 'header>
     }

  member this.IsZero() =
    this.FromSequenceId = 0u || this.ToSequenceId = 0u

  member this.IsValid() = not (this.IsZero())

  // appends chunk1 to chunk0
  // chunk 0 ToSequence must be exactly 1 less than chunk1 FromSequence
  static member Append (chunk0: StreamChunk<'id, 'event, 'header>) (chunk1: StreamChunk<'id, 'event, 'header>) =

    if (chunk0.ToSequenceId  + 1u = chunk1.FromSequenceId) then
      if chunk0.IsZero() then chunk1
      else
      { StreamId = chunk0.StreamId
        FromSequenceId = chunk1.FromSequenceId
        ToSequenceId = chunk0.ToSequenceId
        Events = chunk0.Events @ chunk1.Events
        // StreamChunk = chunk0.StreamChunk
        }
    else
      failwith $"Cannot append chunks. Source chunk version = {chunk0.FromSequenceId}, appending chunk version = {chunk1.ToSequenceId}"

  static member Zero =
    let events: StorableEvent<'id, 'event, 'header> list = []
    { StreamId = Unchecked.defaultof<'id>
      FromSequenceId = 0u
      ToSequenceId = 0u
      Events = events
      // StreamChunk = Unchecked.defaultof<StorableStream<'id, 'event, 'header>>
      }

module Storable =
  open System.Linq
  let toStorableEvent (result: EventEnvelope<'id, 'event, 'header>) =
    //TODO: here maybe add a converter?
    let causation =
      result.CausationId
      |> Option.map (fun causationId ->
        match causationId with
        | CausationId.CommandId id -> StorableCausationId(Type = CausationType.Command, Id = id.Value())
        | CausationId.EventId id -> StorableCausationId(Type = CausationType.Event, Id = id.Value()))

    let result =
      StorableEvent<'id, 'event, 'header>(
        StreamId = result.StreamId,
        EventId = result.EventId,
        Version = result.Version,
        Timestamp = result.Timestamp,
        CausationId = causation,
        CorrelationId = result.CorrelationId,
        Data = result.Payload,
        Header = result.Header
      )

    if not (result.HasValidVersion())
    then
      failwith ("event is invalid version " + result.Version.ToString())

    result

  let toStorableStream (result: Stream<'id, 'event, 'header>) =
    let stream =
      StorableStream<'id, 'event, 'header>(
        Id = result.Id,
        Version = result.Version,
        Created = result.Created, //,
        Modified = result.Modified
      // Children = result.Events |> List.map toStorableEvent |> List.toSeq
      )

    if not (stream.IsValid()) then
      failwith "stream is not valid"

    stream

  let toEvent (storableEvent: StorableEvent<'id, 'event, 'header>) =
    if storableEvent.Header = null then failwith "Header is null"
    let result: EventEnvelope<'id, 'event, 'header> =
      {
        SequenceId = storableEvent.SequenceId
        StreamId = storableEvent.StreamId
        EventId = storableEvent.EventId
        Payload = storableEvent.Data
        CausationId =
          storableEvent.CausationId
          |> Option.map (fun causation ->
            match causation.Type with
            | CausationType.Command ->
              causation.Id
              |> CommandId.FromRawValue
              |> CausationId.CommandId
            | CausationType.Event ->
              causation.Id
              |> EventId.FromRawValue
              |> CausationId.EventId
            | _ -> ArgumentOutOfRangeException() |> raise)
        CorrelationId = storableEvent.CorrelationId
        Version = storableEvent.Version
        Timestamp = storableEvent.Timestamp
        Header = storableEvent.Header
      }

    result

  let toStream (this: StorableStream<'id, 'event, 'header>) =
    let result: Stream<'id, 'event, 'header> =
      { Id = this.Id
        Version = this.Version
        Created = this.Created
        Modified = this.Modified
        Events = this.Children |> Seq.map toEvent |> ResizeArray }

    result

  let chunkToStream(streamChunk: StreamChunk<'id, 'event, 'header>) =
    let events = streamChunk.Events |> List.map toEvent
    if( events.Head.Version <> 1u) then failwith "First event must have version 1"
    let created = events.Head.Timestamp
    let modified = events.Last().Timestamp
    { Id = streamChunk.StreamId
      Version = streamChunk.ToSequenceId
      Created = created
      Modified = modified
      Events = events |> ResizeArray
      }
