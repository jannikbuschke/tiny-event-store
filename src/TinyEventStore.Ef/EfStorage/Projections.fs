module TinyEventStore.EfStorage.Projection

open Microsoft.EntityFrameworkCore
open TinyEventStore.Interfaces

type DeriveProjection<'a,'b,'c,'t,'db when 'db :> DbContext and 'c:>IEvent> =
  {
    Derive:AppendEventsResult<'a,'b,'c> -> 't
    ShouldDelete:AppendEventsResult<'a,'b,'c>->bool
    }

let defaultEfProjection (db: 'db :> DbContext) (obj: 't) (arg: AppendEventsResult<_, _, _>) shouldDelete =
  let set = db.Set<'t>()
  let isNew = arg.IsNew
  match isNew, shouldDelete with
  | true, true -> ()//do nothing
  | true, false ->
    set.Add obj |> ignore
  | false, true ->
    set.Remove obj |> ignore
  | false,false ->
    set.Update obj |> ignore

let handler (db:'db when 'db :> DbContext) (arg: AppendEventsResult<_, _, _>) (proj:DeriveProjection<_,_,_,_,_>)=
  task {
    let obj = proj.Derive arg
    let shouldDelete = proj.ShouldDelete arg
    defaultEfProjection db obj arg shouldDelete
    return ()
  }

// type IEfProjection<'id, 'state, 'event, 'header, 'Db when 'Db :> DbContext> =
//   abstract member Apply: IServiceProvider -> OperationResult<'id, 'state, 'event, 'header> -> unit

// type EfProjection<'Db>(ctx: IServiceProvider, f: OperationResult<'id, 'state, 'event, 'header> -> 'a) =
//   interface IProjection with
//     member _.Apply op =
//       // op.IsNew
//       let db = ctx.GetRequiredService<'Db>()
//       // let dbOp0 = op |> getDefaultDbOperation
//       // let dbOp = dbOp0 |> mapToEfContextOperation db
//       // let a = op |> f
//       // a |> dbOp
//       Task.CompletedTask
