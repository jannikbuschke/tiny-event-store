namespace TinyEventStore.EfSimpleStorage.Dtos

open System
open System.Collections.Generic
open TinyEventStore.InterfacesSimple

[<AbstractClass>]
type StreamBaseDto() =

  member val Id: Guid = Unchecked.defaultof<Guid> with get, set
  member val Version = Unchecked.defaultof<V> with get, set
  member val IsDeleted = Unchecked.defaultof<bool> with get, set
  member val Created = Unchecked.defaultof<DateTimeOffset> with get, set
  member val Modified = Unchecked.defaultof<DateTimeOffset> with get, set

  member this.HasValidVersion() = this.Version > V.zero

  member this.IsValid() =
    this.HasValidVersion() && this.Id <> Guid.Empty

  interface IStreamDbo with
    member this.Created: DateTimeOffset = this.Created
    member this.Modified: DateTimeOffset = this.Modified
    member this.Version: V = this.Version
    member this.Id = this.Id

and StreamDto<'event>() =
  inherit StreamBaseDto()

  member val Children = Unchecked.defaultof<ICollection<EventDto<'event>>> with get, set

  static member Create
    (id: Guid, version: V, isDeleted: bool, created: DateTimeOffset, modified: DateTimeOffset, children)
    =
    StreamDto(
      Id = id,
      Version = version,
      IsDeleted = isDeleted,
      Created = created,
      Modified = modified,
      Children = children
    )

and CausationType =
  | Command = 1uy
  | Event = 2uy

and [<AllowNullLiteral>] StorableCausationId() =
  member val Type = Unchecked.defaultof<CausationType> with get, set
  member val Id = Unchecked.defaultof<Guid> with get, set

and [<AbstractClass>] EventBaseDto() =

  member val Id = Unchecked.defaultof<Guid> with get, set

  member val IsDeleted = Unchecked.defaultof<bool> with get, set
  member val Version = Unchecked.defaultof<V> with get, set
  member val Timestamp = Unchecked.defaultof<DateTimeOffset> with get, set
  member val CausationId = Unchecked.defaultof<StorableCausationId option> with get, set
  member val CorrelationId = Unchecked.defaultof<TinyEventStore.CorrelationId option> with get, set

  member this.HasValidVersion() = this.Version > V.zero

and EventDto<'event>() =
  inherit EventBaseDto()

  member val Data = Unchecked.defaultof<string> with get, set
  member val StreamId = Unchecked.defaultof<Guid> with get, set
  member val Stream = Unchecked.defaultof<StreamDto<'event>> with get, set
