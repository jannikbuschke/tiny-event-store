namespace TinyEventStore

open System

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
