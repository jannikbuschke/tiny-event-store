module TinyEventStore.EfUtils

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

let mapToDbOperation (db: DbContext) =
  function
  | DbSideEffect.Create -> db.Add >> ignore
  | DbSideEffect.Update -> db.Update >> ignore
  | DbSideEffect.Delete -> db.Remove >> ignore
  | DbSideEffect.DoNothing -> fun _ -> ()

let getDefaultDbOperation (operationResult: OperationResult<_, _, _, _>) =
  let isNew = operationResult.New.IsNew()
  let shouldDelete = operationResult.ShouldDelete

  match isNew, shouldDelete with
  | true, true -> DbSideEffect.DoNothing
  | true, false -> DbSideEffect.Create
  | false, true -> DbSideEffect.Delete
  | false, false -> DbSideEffect.Update
