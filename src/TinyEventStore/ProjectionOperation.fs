namespace TinyEventStore.Core

[<RequireQualifiedAccess>]
type DbSideEffect =
  | Create
  | Update
  | Delete
  | DoNothing
