module TinyEventStore.EfSimpleStorage.Projection

open Microsoft.EntityFrameworkCore
open TinyEventStore.InterfacesSimple
open System.Threading.Tasks
open Microsoft.Extensions.Hosting
open System
open TinyEventStore.Ef.Core

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

  // match isNew, shouldDelete with
  // | true, true -> () //do nothing
  // | true, false -> set.Add obj |> ignore
  // | false, true -> set.Remove obj |> ignore
  // | false, false -> set.Update obj |> ignore

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

// generic, could be moved to core
type EfProjection<'s, 'e, 'db> =
  {
    ProjectionDefinition: Projection<'s, 'e>
    // user needs to implement deletion
    OnDeleteProjection: 'db -> Task<unit>
  }

// generic could be moved to core
let projectionStep (projection: Projection<'s, 'e>) (state:'s) (e: 'e) =
  let isNew = projection.isInitializer e
  let zero = projection.zero
  let state0 = zero
  let evolve = projection.evolve
  let state1 = evolve state e
  let isDeleting = projection.isDeleting state0 e
  if isNew then DbSideEffect.Create, state1
  else if isDeleting then DbSideEffect.Delete, state1
  else DbSideEffect.Update, state1

open Microsoft.Extensions.Hosting
open Microsoft.Extensions.DependencyInjection
open System.Threading

let reduceEntityOperation s0 s1 =
  match s0,s1 with
  | DbSideEffect.Create, DbSideEffect.Delete -> DbSideEffect.DoNothing
  | DbSideEffect.Create, DbSideEffect.Update -> DbSideEffect.Create
  | DbSideEffect.Create, DbSideEffect.Create -> DbSideEffect.Create
  | DbSideEffect.Update, DbSideEffect.Update -> DbSideEffect.Update
  | DbSideEffect.Update, DbSideEffect.Create -> DbSideEffect.Update
  | DbSideEffect.Update, DbSideEffect.Delete -> DbSideEffect.Delete
  | DbSideEffect.Delete, DbSideEffect.Create -> DbSideEffect.Update
  | DbSideEffect.Delete, DbSideEffect.Update -> DbSideEffect.Delete
  | DbSideEffect.Delete, DbSideEffect.Delete -> DbSideEffect.Delete
  | DbSideEffect.DoNothing, _ ->  s1
  | _ , DbSideEffect.DoNothing -> s0

let applyEvents projection id state0 events =
  let ops, sx =
    events |> List.fold(fun (op0,s0) e ->
      let op,s1 = projectionStep projection state0 e
      let op1 = reduceEntityOperation op0 op
      op1,s1
    ) (DbSideEffect.DoNothing,state0)
  ops, sx

let restartProjection<'s, 'e, 'sp, 'db when 'db :> DbContext>(
  storage: ISimpleEventStorage<'s,'e>,
  db: 'db,
  efProjection: EfProjection<'sp,'e,'db>) =
  task {
      do! efProjection.OnDeleteProjection db
      let! events = storage.LoadEventRangeAcrossStreams(0, 500)
      return events
        |> List.groupBy storage.GetStreamKey
        |> Seq.map(fun (streamId,events) ->
          applyEvents efProjection.ProjectionDefinition streamId efProjection.ProjectionDefinition.zero events)

      // let grouped = events |> List.groupBy storage.GetStreamKey
      // return
      //   grouped
      //   |> Seq.map(fun (streamId,events) ->
      //     applyEvents efProjection.ProjectionDefinition streamId efProjection.ProjectionDefinition.zero events)
  }

type ProjectionApply<'e,'db,'sp> = EfProjection<'sp,'e,'db> -> 'db -> 'e -> Task

// let myApply:ProjectionApply<_,EventDb_,_> = fun projection db e -> task {
//   let backgroundListItems = db.BackgroundListItems()
//   return ()
//   }

// let apply<'s, 'e, 'sp, 'db when 'db :> DbContext>(
//   storage: ISimpleEventStorage<'s,'e>,
//   db: 'db,
//   efProjection: EfProjection<'sp,'e,'db>,
//   e: 'e
//   ) =
//   task {
//       let streamKey = storage.GetStreamKey e
//       let obj = db.Set<'sp>().FindAsync(streamKey)
//       do! efProjection.OnDeleteProjection db
//       let! events = storage.LoadEventRangeAcrossStreams(0, 500)
//       return events
//         |> List.groupBy storage.GetStreamKey
//         |> Seq.map(fun (streamId,events) ->
//           applyEvents efProjection.ProjectionDefinition streamId efProjection.ProjectionDefinition.zero events)
//       // let grouped = events |> List.groupBy storage.GetStreamKey
//       // return
//       //   grouped
//       //   |> Seq.map(fun (streamId,events) ->
//       //     applyEvents efProjection.ProjectionDefinition streamId efProjection.ProjectionDefinition.zero events)
//   }

type IProjectionService<'e> =
  abstract member Restart: CancellationToken -> Task
  abstract member Apply: 'e * CancellationToken -> Task

// 's is the default projection state
type EfProjectionService<'s, 'e, 'sp, 'db when 'db :> DbContext>(
    provider: IServiceProvider,
    efProjection: EfProjection<'sp,'e,'db>,
    apply: ProjectionApply<'e,'db,'sp>
  ) =
  interface IProjectionService<'e> with
    member this.Restart _ =
      task {
        use scope = provider.CreateScope()
        let storage = scope.ServiceProvider.GetRequiredService<ISimpleEventStorage<'s, 'e>>()
        let db = scope.ServiceProvider.GetRequiredService<'db>()
        let! restartResult = restartProjection(storage, db, efProjection)
        return ()
      }
    member this.Apply(e: 'e, _) = task {
        use scope = provider.CreateScope()
        let storage = scope.ServiceProvider.GetRequiredService<ISimpleEventStorage<'s, 'e>>()
        let db = scope.ServiceProvider.GetRequiredService<'db>()
        do! apply efProjection db e
        return ()
      }

type ProjectionsHostService<'e>(provider: IServiceProvider, channel: System.Threading.Channels.ChannelReader<'e>) =
  inherit BackgroundService()
  override this.ExecuteAsync token =
    task {
      while true do
        let! e = channel.ReadAsync token
        use scope = provider.CreateScope()
        let projectionsService = scope.ServiceProvider.GetRequiredService<IProjectionService<'e>>()
        do! projectionsService.Apply(e, token)

      return ()

    }

// type EventProcessorBackgrundService(services: IServiceProvider) =
//   inherit BackgroundService()
//
//   let handleList
//     (
//       scope: IServiceProvider,
//       id,
//       v: list<NotesWorkflowInstanceEventDetails * EventHeader * option<TinyEventStore.Causation>>
//     )
//     =
//     task {
//       for e, _, causation in v do
//         let! _ = EventStore2.handleEvent scope (id, e, causation)
//         ()
//       return ()
//     }
//
//   let processEvents
//     (events: list<NotesWorkflowInstanceEvent>)
//     (state: NotesWorkflowInstance)
//     (services: IServiceProvider)
//     =
//     [
//       for e in events do
//         async {
//           use scope = services.CreateScope()
//           return! processEvent scope.ServiceProvider state e |> Async.AwaitTask
//         }
//     ]
//
//   let run ct =
//     task {
//       let mutable running = true
//       while running do
//         try
//           let channel = EventStore2.channel
//           let! x = channel.Reader.WaitToReadAsync ct
//           if x then
//             let! read = channel.Reader.ReadAsync ct
//             let events = read.Events
//             let state = read.State
//             // let processResults = processEvents events state services
//
//             for e in events do
//               use scope = services.CreateScope()
//               let! result = processEvent scope.ServiceProvider state e
//               // let! x = result |> Result.map
//               match result with
//               | Ok ok ->
//
//                 // do! handleList (scope.ServiceProvider, read.Id, ok)
//
//                 // let result =
//                 //   ok
//                 //   |> List.map (fun (e, _, causation) ->
//                 //     async {
//                 //     let! _ =
//                 //       EventStore2.handleEvent scope.ServiceProvider (read.Id, e, causation)
//                 //       |> Async.AwaitTask
//                 //     return ()
//                 //   }
//                 // )
//
//                 for e, _, causation in ok do
//                   let! _ = EventStore2.handleEvent scope.ServiceProvider (read.Id, e, causation)
//                   ()
//               | Error error ->
//                 logger.error (
//                   Log.setMessage "EventStore could not handle event {id} {name}: {error}"
//                   >> Log.addParameter e.EventId
//                   >> Log.addParameter (e.Details.GetType().Name)
//                   >> Log.addParameter error
//                   >> Log.addContext "State" state
//                   >> Log.addContext "Event" e
//                 )
//
//               ()
//           else
//             running <- false
//         with
//         | :? OperationCanceledException as _ ->
//           running <- false
//           ()
//         | e ->
//           logger.error (Log.setMessage "Error in EventProcessor" >> Log.addExn e)
//           printfn "Error %A" e
//           running <- false
//       printfn "Event processor background service ended"
//     }
//
//   override _.ExecuteAsync ct = run ct

