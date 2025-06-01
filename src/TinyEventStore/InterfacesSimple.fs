namespace TinyEventStore.InterfacesSimple

open System
open System.Threading.Tasks

type V = int64
module V =
  let zero: V = 0
  let increment (v: V) = v + 1L
  let incrementBy (v: V, i: int) = v + int64 i
type Evolve<'s, 'e> = 's -> 'e -> 's
type CreateEventsContext =
  {
    StreamId: Guid
    TimeStamp: DateTimeOffset
    Version: V
  }

type NonEmptyList<'t> =
  {
    Head: 't
    Tail: 't list
  }

  member this.List = this.Head :: this.Tail
  member this.Last() =
    match this.Tail with
    | [] -> this.Head
    | list -> list |> List.last

module NonEmptyList =
  let From list =
    match list with
    | [] -> Error "List ist empty"
    | head :: tail ->
      Ok
        {
          Head = head
          Tail = tail
        }
  let UnsafeFrom (list: 't list) =
    {
      Head = list.Head
      Tail = list.Tail
    }

type Decide<'s, 'c, 'e> = CreateEventsContext -> 's -> 'c -> Result<NonEmptyList<'e>, string>
type IsDeleted<'s, 'e> = 's -> 'e -> bool
type IsInitialiser<'m> = 'm -> bool

type Projection<'s, 'e> = Evolve<'s, 'e>

type Aggregate<'s, 'e> =
  {
    zero: 's
    evolve: Evolve<'s, 'e>
    isDeleting: IsDeleted<'s, 'e>
  }

type EventStoreDefinition<'state, 'e, 'c> =
  {
    aggregate: Aggregate<'state, 'e>
    decide: Decide<'state, 'c, 'e>
    isEventInitialiser: IsInitialiser<'e>
    isCommandInitializer: IsInitialiser<'c>
    getEventVersion: 'e -> V
  }

[<RequireQualifiedAccess>]
type InitializationError =
  | EventIsNotInitializer
  | CommandIsNotInitializer
  | Other

[<RequireQualifiedAccess>]
type IllegalArgumentError = | EmptyEvents

[<RequireQualifiedAccess>]
type EventStoreErrorDetails =
  | NotInitialized
  | NotFound
  | IllegalArgument of IllegalArgumentError
  | InitializationError of InitializationError
  | CommitError
  | DecideError of string
  | ConstructEventsError of string

type EventStoreError =
  {
    Message: string option
    Details: EventStoreErrorDetails
  }

  static member New(details, message) =
    {
      Message = message
      Details = details
    }

type ApplyResult = Task<Result<unit, EventStoreError>>

type AppendEventsResult<'state, 'e> =
  {
    Id: Guid
    Version: V
    State: 'state
    Events: NonEmptyList<'e>
    IsNew: bool
    TimeStamp: DateTimeOffset
  }

type IProjection =
  abstract member Apply: AppendEventsResult<'state, 'e> -> Task

type HydrationResultOk<'s, 'e> =
  {
    State: 's
    Events: NonEmptyList<'e>
    Version: V
  }

[<RequireQualifiedAccess>]
type HydrationResult<'s, 'e> =
  | NotStarted
  | Value of HydrationResultOk<'s, 'e>

  member this.ValueOrError() =
    match this with
    | NotStarted -> Error()
    | Value value -> Ok value

type IStreamDbo =
  abstract member Id: Guid
  abstract member Version: V
  abstract member Created: DateTimeOffset
  abstract member Modified: DateTimeOffset

type ISimpleEventStorage<'state, 'event, 'command> =
  abstract member LoadEventRange: Guid * DateTimeOffset * DateTimeOffset -> Task<NonEmptyList<'event> option>
  abstract member LoadAllEvents: Guid -> Task<NonEmptyList<'event> option>
  abstract member Commit: AppendEventsResult<'state, 'event> -> Task<Result<unit, EventStoreError>>
  abstract member LoadStream: Guid -> Task<Result<IStreamDbo, EventStoreError>>

type OnCommittingEventHandler<'b, 'c, 'ctx> = 'ctx -> AppendEventsResult<'b, 'c> -> Task
