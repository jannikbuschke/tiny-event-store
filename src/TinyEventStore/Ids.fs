namespace TinyEventStore

open System

// type Id<'Entity> = | Id of Guid
//
// module Id =
//   let create () = Id(Guid.NewGuid())
//   let value (Id guid) = guid
//   let from (raw: Guid) = Id raw
//   let fromRaw (raw: string) = raw |> Guid.Parse |> Id
//
// type EventEntity = interface end
// type EventId = Id<EventEntity>

[<RequireQualifiedAccess>]
type DomainEventId =
  | DomainEventId of Guid
  member this.Value() = this |> DomainEventId.ToRawValue
  static member New() = DomainEventId(Guid.NewGuid())
  static member ToRawValue(DomainEventId rawValue) = rawValue
  static member FromRawValue(rawValue: Guid) = DomainEventId rawValue

[<RequireQualifiedAccess>]
type EventId =
  | EventId of Guid
  member this.Value() = this |> EventId.ToRawValue
  static member New() = EventId(Guid.NewGuid())
  static member ToRawValue(EventId rawValue) = rawValue
  static member FromRawValue(rawValue: Guid) = EventId rawValue

[<RequireQualifiedAccess>]
type CorrelationId =
  | CorrelationId of Guid

  member this.Value() = this |> CorrelationId.ToRawValue

  static member New() = CorrelationId(Guid.NewGuid())
  static member ToRawValue(CorrelationId rawValue) = rawValue
  static member FromRawValue(rawValue: Guid) = CorrelationId rawValue

[<RequireQualifiedAccess>]
type CommandId =
  | CommandId of Guid

  member this.Value() = this |> CommandId.ToRawValue

  static member New() = CommandId(Guid.NewGuid())
  static member ToRawValue(CommandId rawValue) = rawValue
  static member FromRawValue(rawValue: Guid) = CommandId rawValue

[<RequireQualifiedAccess>]
type CausationId =
  | CommandId of CommandId
  | EventId of EventId
  // | DomainEventId of DomainEventId

// type Causation =
//   | DomainEvent of DomainEventId* EventName:string
//   | Message of EntityType:string * StreamId * CausationId

type Causation={
    MessageName: string// CommandName, EventName, DomainEventName
    EntityType: string option
    Id: CausationId option
  }

// [CommandId;EventId;DomainEventId] [CommandName;EventName;DomainEventName] [EntityTypeName]
