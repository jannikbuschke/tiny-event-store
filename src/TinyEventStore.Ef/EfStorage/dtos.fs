namespace TinyEventStore.EfStorage.Dtos

open System
open System.Collections.Generic
open TinyEventStore.Interfaces

[<AbstractClass>]
type StreamBaseDto<'id when 'id: equality>() =

  member val Id: 'id = Unchecked.defaultof<'id> with get, set

  member val Version = Unchecked.defaultof<V> with get, set
  member val IsDeleted = Unchecked.defaultof<bool> with get, set
  member val Created = Unchecked.defaultof<DateTimeOffset> with get, set
  member val Modified = Unchecked.defaultof<DateTimeOffset> with get, set

  member this.HasValidVersion() = this.Version > Version.Zero

  member this.IsValid() =
    this.HasValidVersion() && this.Id <> Unchecked.defaultof<'id>

and StreamDto<'streamIdRaw, 'event when 'streamIdRaw: equality>() =
  inherit StreamBaseDto<'streamIdRaw>()

  member val Children = Unchecked.defaultof<ICollection<EventDto<'streamIdRaw, 'event>>> with get, set

  static member Create(id:'id,version:V,isDeleted:bool,created:DateTimeOffset,modified:DateTimeOffset,children)=
    StreamDto(Id=id,Version=version,IsDeleted=isDeleted,Created=created,Modified=modified,Children=children)

and CausationType =
  | Command = 1uy
  | Event = 2uy

and [<AllowNullLiteral>] StorableCausationId() =
  member val Type = Unchecked.defaultof<CausationType> with get, set
  member val Id = Unchecked.defaultof<Guid> with get, set

and [<AbstractClass>] EventBaseDto<'id>() =

  member val EventId = Unchecked.defaultof<'id> with get, set

  member val SequenceId = Unchecked.defaultof<SequenceId> with get, set
  member val IsDeleted = Unchecked.defaultof<bool> with get, set
  member val Version = Unchecked.defaultof<V> with get, set
  member val Timestamp = Unchecked.defaultof<DateTimeOffset> with get, set
  member val CausationId = Unchecked.defaultof<StorableCausationId option> with get, set
  member val CorrelationId = Unchecked.defaultof<TinyEventStore.CorrelationId option> with get, set

  member this.HasValidSequenceId() = this.SequenceId > SequenceId.Zero
  member this.HasValidVersion() = this.Version > Version.Zero

  member this.IsValid() =
    this.HasValidSequenceId() && this.HasValidVersion()

  member this.InvalidReason() =
    if this.IsValid() then
      None
    else
      let versionIsValid = this.HasValidVersion()
      let sequenceIsValid = this.HasValidSequenceId()
      Some $"Invalid event. Version is valid = {versionIsValid}, Sequence is valid = {sequenceIsValid}"

and EventDto<'streamIdRaw, 'event when 'streamIdRaw: equality>() =
  inherit EventBaseDto<Guid>()

  member val Data = Unchecked.defaultof<'event> with get, set
  member val StreamId = Unchecked.defaultof<'streamIdRaw> with get, set
  member val Stream = Unchecked.defaultof<StreamDto<'streamIdRaw, 'event>> with get, set
