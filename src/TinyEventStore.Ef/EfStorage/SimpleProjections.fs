module TinyEventStore.EfSimpleStorage.Projection

open Microsoft.EntityFrameworkCore
open TinyEventStore.InterfacesSimple
open System.Threading.Tasks
open Microsoft.Extensions.Hosting
open System

type DeriveProjection<'state, 'event, 't, 'db when 'db :> DbContext> =
  {
    Derive: AppendEventsResult<'state, 'event> -> 't
    ShouldDelete: AppendEventsResult<'state, 'event> -> bool
  }

let defaultEfProjection (db: 'db :> DbContext) (obj: 't) (arg: AppendEventsResult<_, _>) shouldDelete =
  let set = db.Set<'t>()
  let isNew = arg.IsNew
  if isNew && shouldDelete then
    () // no-op
  else if isNew && not shouldDelete then
    set.Add obj |> ignore
  else if not isNew && shouldDelete then
    set.Remove obj |> ignore
  else if not isNew && not shouldDelete then
    set.Update obj |> ignore

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

// type FunctionProjection<'state, 't, 'db when 'db :> DbContext> = 'state * 't * 'db -> Task<unit>

// type NewProjection<'state, 'event, 't, 'db when 'db :> DbContext> =
//   {
//     Derive: AppendEventsResult<'state, 'event> -> 't
//     ShouldDelete: AppendEventsResult<'state, 'event> -> bool
//   }

[<CLIMutable>]
type ProjectionState =
  {
    Id: string
    Version: V
  }

type EfProjection<'s, 'e, 'db> =
  {
    ProjectionDefinition: Projection<'s, 'e>
    // user needs to implement deletion
    OnDeleteProjection: 'db -> Task<unit>
  }

let projectionStep (projection: Projection<'s, 'e>) (e: 'e) (db) =
  let isNew = projection.isInitializer e
  let zero = projection.zero
  let state0 = zero
  let evolve = projection.evolve
  let state1 = evolve zero e
  let isDeleting = projection.isDeleting state0 e

  ()

open Microsoft.Extensions.Hosting
open Microsoft.Extensions.DependencyInjection

type ProjectionService<'db, 's, 'e>(provider: IServiceProvider) =
  member this.ApplyEvent (e: 'e) token =
    task {
      use scope = provider.CreateScope()
      let db = scope.ServiceProvider.GetRequiredService<'db>()
      return ()
    }

type ProjectionsHostService(provider: IServiceProvider) =
  inherit BackgroundService()
  override this.ExecuteAsync token =
    task {
      use scope = provider.CreateScope()
      scope.ServiceProvider.GetRequiredService<'db>()
    }
