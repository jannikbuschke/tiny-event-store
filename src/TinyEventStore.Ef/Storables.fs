module TinyEventStore.Ef.Storables

open System
open System.Collections.Generic
open TinyEventStore

[<AbstractClass>]
type AbstractStorableStream<'id when 'id: equality>() =
  abstract member Id: 'id with get, set
  member val Version = Unchecked.defaultof<uint32> with get, set // TODO: this is never initialized
  member val IsDeleted = Unchecked.defaultof<bool> with get, set
  member val Created = Unchecked.defaultof<DateTimeOffset> with get, set
  member val Modified = Unchecked.defaultof<DateTimeOffset> with get, set

  member this.HasValidVersion() = this.Version > 0u

  member this.IsValid() =
    this.HasValidVersion() && (this.Id <> Unchecked.defaultof<'id>)

and StorableStream<'id, 'event, 'header when 'id: equality>() =
  inherit AbstractStorableStream<'id>()
  let mutable id = Unchecked.defaultof<'id>
  override _.Id = id

  override _.Id
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
  member val IsDeleted = Unchecked.defaultof<bool> with get, set
  member val EventId = Unchecked.defaultof<EventId> with get, set
  member val Version = Unchecked.defaultof<uint32> with get, set
  member val Timestamp = Unchecked.defaultof<DateTimeOffset> with get, set
  member val CausationId = Unchecked.defaultof<StorableCausationId option> with get, set
  member val CorrelationId = Unchecked.defaultof<CorrelationId option> with get, set
  member this.HasValidSequenceId() = this.SequenceId > 0ul
  member this.HasValidVersion() = this.Version > 0u

  member this.IsValid() =
    this.HasValidSequenceId() && this.HasValidVersion()

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
    IsDeleted: bool
    FromSequenceId: uint32
    ToSequenceId: uint32
    Events: StorableEvent<'id, 'event, 'header> list }

  member this.IsZero() =
    this.FromSequenceId = 0u || this.ToSequenceId = 0u

  member this.IsValid() = not (this.IsZero())
  // appends chunk1 to chunk0
  // chunk 0 ToSequence must be exactly 1 less than chunk1 FromSequence
  // static member Append (chunk0: StreamChunk<'id, 'event, 'header>) (chunk1: StreamChunk<'id, 'event, 'header>) =
  //
  //   if (chunk0.Version + 1u = chunk1.FromSequenceId) then
  //     // if chunk0.IsZero() then
  //     //   chunk1
  //     // else
  //       chunk0.Events.Append()
  //       {
  //       chunk0 with
  //         Stream.Version = chunk1.ToSequenceId
  //         Events = chunk1.Events |> Seq.append chunk0.Events}
  //       // { StreamId = chunk0.StreamId
  //       //   FromSequenceId = chunk1.FromSequenceId
  //       //   ToSequenceId = chunk0.ToSequenceId
  //       //   Events =  }
  //   else
  //     failwith
  //       $"Cannot append chunks. Source chunk version = {chunk0.FromSequenceId}, appending chunk version = {chunk1.ToSequenceId}"

  static member Zero =
    let events: StorableEvent<'id, 'event, 'header> list = []

    { StreamId = Unchecked.defaultof<'id>
      IsDeleted = false
      FromSequenceId = 0u
      ToSequenceId = 0u
      Events = events }

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
        Header = result.Header,
        IsDeleted = result.IsDeleted
      )

    if not (result.HasValidVersion()) then
      failwith ("event is invalid version " + result.Version.ToString())

    result

  let toStorableStream (result: Stream<'id, 'event, 'header>) =
    let stream =
      StorableStream<'id, 'event, 'header>(
        Id = result.Id,
        Version = result.Version,
        Created = result.Created,
        Modified = result.Modified,
        IsDeleted = result.IsDeleted
      )

    if not (stream.IsValid()) then
      failwith "stream is not valid"

    stream

  let toEvent (storableEvent: StorableEvent<'id, 'event, 'header>) =
    if box storableEvent.Header = null then
      failwith "Header is null"

    let result: EventEnvelope<'id, 'event, 'header> =
      { SequenceId = storableEvent.SequenceId
        IsDeleted = storableEvent.IsDeleted
        StreamId = storableEvent.StreamId
        EventId = storableEvent.EventId
        Payload = storableEvent.Data
        CausationId =
          storableEvent.CausationId
          |> Option.map (fun causation ->
            match causation.Type with
            | CausationType.Command -> causation.Id |> CommandId.FromRawValue |> CausationId.CommandId
            | CausationType.Event -> causation.Id |> EventId.FromRawValue |> CausationId.EventId
            | _ -> ArgumentOutOfRangeException() |> raise)
        CorrelationId = storableEvent.CorrelationId
        Version = storableEvent.Version
        Timestamp = storableEvent.Timestamp
        Header = storableEvent.Header }

    result

  let toStream (this: StorableStream<'id, 'event, 'header>) =
    let result: Stream<'id, 'event, 'header> =
      { Id = this.Id
        Version = this.Version
        IsDeleted = this.IsDeleted
        Created = this.Created
        Modified = this.Modified
        Events = this.Children |> Seq.map toEvent |> ResizeArray }

    result

  let chunkToStream (streamChunk: StreamChunk<'id, 'event, 'header>) =
    let events = streamChunk.Events |> List.map toEvent

    if (events.Head.Version <> 1u) then
      failwith "First event must have version 1"

    let created = events.Head.Timestamp
    let modified = events.Last().Timestamp

    { Id = streamChunk.StreamId
      IsDeleted = streamChunk.IsDeleted
      Version = streamChunk.ToSequenceId
      Created = created
      Modified = modified
      Events = events |> ResizeArray }
