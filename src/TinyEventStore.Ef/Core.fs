module TinyEventStore.EfUtils

open Microsoft.EntityFrameworkCore
open TinyEventStore

[<RequireQualifiedAccess>]
type DbSideEffect =
  | Create
  | Update
  | Delete

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

type IdConverter<'id, 'rawId> = ('id -> 'rawId) * ('rawId -> 'id)
type Converter<'value, 'dto> = ('value -> 'dto) * ('dto -> 'value)
