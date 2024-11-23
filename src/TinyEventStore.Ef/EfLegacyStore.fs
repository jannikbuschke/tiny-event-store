module TinyEventStore.Ef.LegacyStore

open System
open Microsoft.EntityFrameworkCore
open Microsoft.Extensions.DependencyInjection
open TinyEventStore
open Queries
open Core
open TinyEventStore.Ef.Handler
open Store

let efCreate<'id, 'state, 'event, 'header, 'command, 'commandHeader, 'sideEffect, 'Db
  when 'Db :> DbContext and 'id: equality>
  (zero: 'state)
  (evolve: Evolve<'id, 'state, 'event, 'header>)
  (decide: PureDecide<'id, 'state, 'command, 'commandHeader, 'event, 'header, 'sideEffect>)
  : EfStore<'id, 'state, 'command, 'commandHeader, 'event, 'header, 'sideEffect, 'Db> =

  let aggregate =
    { zero = zero
      evolve = evolve
      shouldDelete = fun _ _ -> false }

  let prepare =
    prepare<'id, 'state, 'command, 'commandHeader, 'event, 'header, 'sideEffect, 'Db> aggregate decide

  let rehydrateLatest2 = efRehydrate2<'id, 'state, 'event, 'header, 'Db> zero evolve
  let rehydrateMany = rehydrateMany<'id, 'state, 'event, 'header, 'Db> zero evolve
  let rehydrateAll = rehydrateAll<'id, 'state, 'event, 'header, 'Db> zero evolve
  let appendEvents = efAppendEvents<'id, 'state, 'event, 'header, 'Db> aggregate

  let updateEventStore2 (ctx: IServiceProvider) operationResult =
    let db = ctx.GetService<'Db>()
    updateEventStream2 db operationResult

  { prepare = prepare
    aggregate =
      { zero = zero
        evolve = evolve
        shouldDelete = fun _ _ -> false }
    rehydrateLatest2 = rehydrateLatest2
    rehydrateMany = rehydrateMany
    rehydrateAll = rehydrateAll
    rehydrate = PureStore.rehydrate zero evolve
    getDb = fun ctx -> ctx.GetService<'Db>()
    applyOperationResultToProjections = fun _ _ -> ()
    appendEvents = appendEvents
    updateEventStore2 = updateEventStore2
    replayProjection = fun _ _ -> failwith "Not implemented0"
    applyCommand = fun _ _ -> failwith "Not Implemented1"
    applyEvents = fun _ _ -> failwith "Not Implemented2"
    saveChangesAsync = fun _ -> failwith "Not Implemented3"
    queryStreams = fun _ -> failwith "Not Implemented4"
    saveChangesAsyncWithResult = fun _ -> failwith "Not Implemented3" }
