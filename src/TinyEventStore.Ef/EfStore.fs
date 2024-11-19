module TinyEventStore.Ef.Store

open System
open Microsoft.EntityFrameworkCore
open Microsoft.Extensions.DependencyInjection
open TinyEventStore
open FsToolkit.ErrorHandling
open Queries
open Core
open TinyEventStore.Ef.Projections
open TinyEventStore.Ef.Handler

type EfStore<'id, 'state, 'command, 'commandHeader, 'event, 'header, 'sideEffect, 'Db when 'Db :> DbContext> =
  { prepare:
      IServiceProvider
        -> 'id
        -> TaskResult<
          CommandEnvelope<'id, 'command, 'commandHeader>
            -> Result<CommandResult<'id, 'state, 'event, 'header, 'sideEffect>, string>,
          string
         >
    appendEvents:
      IServiceProvider
        -> 'id
        -> ('event * 'header) list
        -> TaskResult<OperationResult<'id, 'state, 'event, 'header>, string>
    replayProjection: IServiceProvider -> IEfProjection<'id, 'state, 'event, 'header, 'Db> -> TaskResult<unit, string>
    rehydrateLatest2: IServiceProvider -> 'id -> TaskResult<'state * Stream<'id, 'event, 'header>, string>
    rehydrateMany: IServiceProvider -> 'id list -> TaskResult<('state * Stream<'id, 'event, 'header>) list, string>
    rehydrateAll: IServiceProvider -> TaskResult<('state * Stream<'id, 'event, 'header>) list, string>
    rehydrate: Stream<'id, 'event, 'header> -> 'state
    getDb: IServiceProvider -> 'Db
    updateEventStore2: IServiceProvider -> OperationResult<'id, 'state, 'event, 'header> -> unit
    applyOperationResultToProjections: IServiceProvider -> OperationResult<'id, 'state, 'event, 'header> -> unit
    applyCommand:
      IServiceProvider
        -> ('id * CommandEnvelope<'id, 'command, 'commandHeader>)
        -> TaskResult<CommandResult<'id, 'state, 'event, 'header, 'sideEffect>, string>
    applyEvents:
      IServiceProvider
        -> 'id * ('event * 'header) list
        -> TaskResult<OperationResult<'id, 'state, 'event, 'header>, string>
    aggregate: Aggregate<'id, 'state, 'event, 'header>
    saveChangesAsync: IServiceProvider -> TaskResult<unit, string>
    saveChangesAsyncWithResult: IServiceProvider -> TaskResult<int, string> }

type Configuration() =
  static member Configure<'id, 'state, 'event, 'header, 'command, 'commandHeader, 'Db
    when 'Db :> DbContext and 'id: equality>
    (
      aggregate: Aggregate<'id, 'state, 'event, 'header>,
      decide: PureDecide<'id, 'state, 'command, 'commandHeader, 'event, 'header, unit>,
      projections: IEfProjection<'id, 'state, 'event, 'header, 'Db> list
    ) : EfStore<'id, 'state, 'command, 'commandHeader, 'event, 'header, unit, 'Db> =
    let zero = aggregate.zero
    let evolve = aggregate.evolve

    let prepare =
      prepare<'id, 'state, 'command, 'commandHeader, 'event, 'header, unit, 'Db> aggregate decide

    let rehydrateLatest2 = efRehydrate2<'id, 'state, 'event, 'header, 'Db> zero evolve
    let rehydrateMany = rehydrateMany<'id, 'state, 'event, 'header, 'Db> zero evolve
    let rehydrateAll = rehydrateAll<'id, 'state, 'event, 'header, 'Db> zero evolve
    let appendEvents = efAppendEvents<'id, 'state, 'event, 'header, 'Db> aggregate
    let replayProjection = rerunProject<'id, 'state, 'event, 'header, 'Db> aggregate // ctx // projection.Apply

    let applyResultToProjections serviceProvider result =
      projections |> List.iter (fun p -> p.Apply serviceProvider result)

    let updateEventStore2 (ctx: IServiceProvider) operationResult =
      let db = ctx.GetService<'Db>()
      updateEventStream2 db operationResult

    let applyEvents (ctx: IServiceProvider) (streamId, events) =
      taskResult {
        let! result = appendEvents ctx streamId events
        updateEventStore2 ctx result
        applyResultToProjections ctx result
        return result
      }

    let applyCommand serviceProvider (id, command) =
      taskResult {
        let! run = prepare serviceProvider id
        let! result = run command
        updateEventStore2 serviceProvider result
        applyResultToProjections serviceProvider result
        return result
      }

    { prepare = prepare
      applyOperationResultToProjections = applyResultToProjections
      aggregate = aggregate
      rehydrateLatest2 = rehydrateLatest2
      rehydrateMany = rehydrateMany
      rehydrateAll = rehydrateAll
      rehydrate = PureStore.rehydrate zero evolve
      getDb = fun ctx -> ctx.GetService<'Db>()
      appendEvents = appendEvents
      applyCommand = applyCommand
      applyEvents = applyEvents
      updateEventStore2 = updateEventStore2
      replayProjection =
        fun ctx projection ->
          taskResult {
            let! result = replayProjection ctx projection
            return ()
          }

      saveChangesAsyncWithResult =
        fun (ctx) ->
          taskResult {
            let db = ctx.GetService<'Db>()
            let! r = db.SaveChangesAsync()
            return r
          }

      saveChangesAsync =
        fun (ctx) ->
          taskResult {
            let db = ctx.GetService<'Db>()
            let! _ = db.SaveChangesAsync()
            return ()
          } }
