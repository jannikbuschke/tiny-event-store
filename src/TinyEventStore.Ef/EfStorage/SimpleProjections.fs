module TinyEventStore.EfSimpleStorage.Projection

open Microsoft.EntityFrameworkCore
open TinyEventStore.InterfacesSimple

type DeriveProjection<'state, 'event, 't, 'db when 'db :> DbContext> =
  {
    Derive: AppendEventsResult<'state, 'event> -> 't
    ShouldDelete: AppendEventsResult<'state, 'event> -> bool
  }

let defaultEfProjection (db: 'db :> DbContext) (obj: 't) (arg: AppendEventsResult<_, _>) shouldDelete =
  let set = db.Set<'t>()
  let isNew = arg.IsNew
  match isNew, shouldDelete with
  | true, true -> () //do nothing
  | true, false -> set.Add obj |> ignore
  | false, true -> set.Remove obj |> ignore
  | false, false -> set.Update obj |> ignore

let handler (db: 'db :> DbContext) (arg: AppendEventsResult<_, _>) (proj: DeriveProjection<_, _, _, _>) =
  task {
    let obj = proj.Derive arg
    let shouldDelete = proj.ShouldDelete arg
    defaultEfProjection db obj arg shouldDelete
    return ()
  }
