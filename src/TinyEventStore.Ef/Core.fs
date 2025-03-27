module TinyEventStore.Ef.Core

open Microsoft.EntityFrameworkCore
open TinyEventStore

[<RequireQualifiedAccess>]
type DbSideEffect =
  | Create
  | Update
  | Delete
  | DoNothing

type IdConverter<'id, 'rawId> = ('id -> 'rawId) * ('rawId -> 'id)
type Converter<'value, 'dto> = ('value -> 'dto) * ('dto -> 'value)

let projectToDbCommand (events: EventEnvelope<'id, 'event, 'header> list) =
  if (events.Item 0).Version = 1u then
    DbSideEffect.Create
  else
    DbSideEffect.Update

let mapToEfContextOperation (db: DbContext) =
  function
  | DbSideEffect.Create -> db.Add >> ignore
  | DbSideEffect.Update -> db.Update >> ignore
  | DbSideEffect.Delete -> db.Remove >> ignore
  | DbSideEffect.DoNothing -> fun _ -> ()

let (|IsNew|IsDeleted|ShouldDelete|ShouldUpdate|IsNewAndshouldDelete|) (operationResult: OperationResult<_, _, _, _>) =

  let isNew = operationResult.New.IsNew()
  let shouldDelete = operationResult.ShouldDelete
  let isDeleted = operationResult.IsDeleted

  match isNew, shouldDelete, isDeleted with
  | true, true, _ -> IsNewAndshouldDelete
  | true, false, _ -> IsNew
  | false, true, _ -> ShouldDelete
  | false, false, true -> IsDeleted
  | false, false, false -> ShouldUpdate

let getDefaultDbOperation (operationResult: OperationResult<_, _, _, _>) =
  match operationResult with
  | IsNewAndshouldDelete -> DbSideEffect.DoNothing
  | IsNew -> DbSideEffect.Create
  | IsDeleted ->
    DbSideEffect.DoNothing
  | ShouldDelete -> DbSideEffect.Delete
  | ShouldUpdate -> DbSideEffect.Update
