namespace TinyEventStore.InterfacesSimple

open System
open System.Threading.Tasks
open TinyEventStore.Core

type V = int64

module V =
  let zero: V = 0
  let increment (v: V) = v + 1L
  let incrementBy (v: V, i: int) = v + int64 i

type Evolve<'s, 'e> = 's -> 'e -> 's

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

type CreateEventsContext =
  {
    StreamId: Guid
    TimeStamp: DateTimeOffset
    Version: V
  }

  member this.CreateMultiple(events, f) =
    events
    |> List.mapi (fun i e ->
      let ts = this.TimeStamp.AddMicroseconds i
      f e (Guid.CreateVersion7(ts)) (V.incrementBy (this.Version, i + 1)) ts
    )
    |> NonEmptyList.UnsafeFrom

type Decide<'s, 'c, 'e> = CreateEventsContext -> 's -> 'c -> Result<NonEmptyList<'e>, string>
type IsDeleted<'s, 'e> = 's -> 'e -> bool
type IsInitialiser<'m> = 'm -> bool

type Aggregate<'s, 'e> =
  {
    zero: 's
    evolve: Evolve<'s, 'e>
    isDeleting: IsDeleted<'s, 'e>
  }

type Projection<'s, 'e> =
  {
    zero: 's
    evolve: Evolve<'s, 'e>
    isDeleting: IsDeleted<'s, 'e>
    isInitializer: IsInitialiser<'e>
  }

  member this.Op(s: 's, e: 'e) =
    let isNew = this.isInitializer e
    let shouldDelete = this.isDeleting s e
    if isNew && shouldDelete then
      DbSideEffect.DoNothing
    else if isNew && not shouldDelete then
      DbSideEffect.Create
    else if not isNew && shouldDelete then
      DbSideEffect.Create
    else if not isNew && not shouldDelete then
      DbSideEffect.Update
    else
      DbSideEffect.DoNothing

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
    /// StreamId
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

module Result =
  let requireHydrationValue v =
    match v with
    | HydrationResult.NotStarted -> Error "HydrationResult is NotStarted"
    | HydrationResult.Value v -> Ok v

type IStreamDbo =
  abstract member Id: Guid
  abstract member Version: V
  abstract member Created: DateTimeOffset
  abstract member Modified: DateTimeOffset

// type ISimpleEventStorage<'event> =
//   abstract member LoadEventRange: Guid * DateTimeOffset * DateTimeOffset -> Task<NonEmptyList<'event> option>
//   abstract member LoadEventRangeAcrossStreams: V * V -> Task<'event list>
//   abstract member LoadAllEvents: Guid -> Task<NonEmptyList<'event> option>
//   abstract member Commit: AppendEventsResult<'state, 'event> -> Task<Result<unit, EventStoreError>>
//   abstract member LoadStream: Guid -> Task<Result<IStreamDbo, EventStoreError>>
//   abstract member GetStreamKey: 'event -> Guid

type ISimpleEventStorage<'event> =
  abstract member LoadEventRange: Guid * DateTimeOffset * DateTimeOffset -> Task<NonEmptyList<'event> option>
  abstract member LoadEventRangeAcrossStreams: V * V -> Task<'event list>
  abstract member LoadAllEvents: Guid -> Task<NonEmptyList<'event> option>
  abstract member Commit: AppendEventsResult<'state, 'event> -> Task<Result<unit, EventStoreError>>
  abstract member LoadStream: Guid -> Task<Result<IStreamDbo, EventStoreError>>
  abstract member GetStreamKey: 'event -> Guid

type OnCommittingEventHandler<'state, 'event, 'ctx> = 'ctx -> AppendEventsResult<'state, 'event> -> Task
