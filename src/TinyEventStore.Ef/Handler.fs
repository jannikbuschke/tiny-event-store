module TinyEventStore.Ef.Handler

open System
open Microsoft.EntityFrameworkCore
open Microsoft.Extensions.DependencyInjection
open TinyEventStore
open FsToolkit.ErrorHandling
open Queries
open Storables

let prepare<'id, 'state, 'command, 'ch, 'event, 'header, 'sideEffect, 'Db when 'Db :> DbContext and 'id: equality>
  (aggregate: Aggregate<'id, 'state, 'event, 'header>)
  (executeCommand: PureDecide<'id, 'state, 'command, 'ch, 'event, 'header, 'sideEffect>)
  (ctx: IServiceProvider)
  =
  let db = ctx.GetRequiredService<'Db>()
  let loadEvents = loadStorableStream<'id, 'event, 'header> db

  fun (id: 'id) ->
    id
    |> loadEvents
    |> TaskResult.map (PureStore.makeCommandHandler aggregate executeCommand)

// creates an operation result
let efAppendEvents<'id, 'state, 'event, 'header, 'Db when 'Db :> DbContext and 'id: equality>
  (aggregate: Aggregate<'id, 'state, 'event, 'header>)
  (ctx: IServiceProvider)
  (id: 'id)
  (events: ('event * 'header) list)
  =
  taskResult {
    let db = ctx.GetRequiredService<'Db>()
    let! stream = loadStorableStream<'id, 'event, 'header> db id

    let result =
      PureStore.appendEvents aggregate stream (id, events) :> OperationResult<'id, 'state, 'event, 'header>

    return result
  }

open Core

let updateStorableStreamAndEvents (db: DbContext) (stream: Stream<'id, 'event, 'header>) events =
  let stream = Storable.toStorableStream stream
  let dbCmd = projectToDbCommand events
  let events = events |> List.map Storable.toStorableEvent
  let insertOrUpdate x = mapToEfContextOperation db dbCmd x
  // stream is loaded beforehand, so we can use its entry
  // let entry = db.Entry(stream)
  insertOrUpdate stream
  events |> List.iter (db.Add >> ignore)
  ()

let updateEventStream2 (db: DbContext) (appendEventResult: OperationResult<'id, 'state, 'event, 'eventHeader>) =
  updateStorableStreamAndEvents db appendEventResult.NewStream appendEventResult.NewEvents
