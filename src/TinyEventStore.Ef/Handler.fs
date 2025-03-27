module TinyEventStore.Ef.Handler

open System
open Microsoft.EntityFrameworkCore
open Microsoft.Extensions.DependencyInjection
open TinyEventStore
open FsToolkit.ErrorHandling
open Queries
open Storables
open TinyEventStore.HandleCommandAndEvents
open Core
open System.Linq

let prepare<'id, 'state, 'command, 'ch, 'event, 'header, 'sideEffect, 'Db when 'Db :> DbContext and 'id: equality>
  (aggregate: Aggregate<'id, 'state, 'event, 'header>)
  (executeCommand: PureDecide<'id, 'state, 'command, 'ch, 'event, 'header, 'sideEffect>)
  (ctx: IServiceProvider)
  =
  let db = ctx.GetRequiredService<'Db>()
  let loadEvents = loadStorableStream<'id, 'event, 'header> db

  fun (id: 'id) ->
    (id, None)
    |> loadEvents
    |> TaskResult.map (makeCommandHandler aggregate executeCommand)

// creates an operation result
let efAppendEvents<'id, 'state, 'event, 'header, 'Db when 'Db :> DbContext and 'id: equality>
  (aggregate: Aggregate<'id, 'state, 'event, 'header>)
  (ctx: IServiceProvider)
  (id: 'id)
  (events: ('event * 'header * CausationId option) list)
  =
  taskResult {
    let db = ctx.GetRequiredService<'Db>()
    let! stream = loadStorableStream<'id, 'event, 'header> db (id, None)

    let result =
      appendEvents aggregate stream (id, events) :> OperationResult<'id, 'state, 'event, 'header>

    return result
  }

let updateStorableStreamAndEvents
  (db: DbContext)
  (stream: Stream<'id, 'event, 'header>)
  events
  (operationResult: OperationResult<_, _, _, _>)
  =
  let stream = Storable.toStorableStream stream
  let dbCmd = projectToDbCommand events
  let firstNewEvent = operationResult.New.EventsChunk.Head

  if firstNewEvent.IsInitialEvent() then
    stream.Created <- firstNewEvent.Timestamp

  if operationResult.ShouldDelete then
    stream.IsDeleted <- true

  stream.Modified <- operationResult.New.EventsChunk.Last().Timestamp
  let events = events |> List.map Storable.toStorableEvent
  let insertOrUpdate x = mapToEfContextOperation db dbCmd x
  // stream is loaded beforehand, so we can use its entry
  // let entry = db.Entry(stream)
  insertOrUpdate stream
  events |> List.iter (db.Add >> ignore)
  ()

let updateEventStream2 (db: DbContext) (appendEventResult: OperationResult<'id, 'state, 'event, 'eventHeader>) =
  updateStorableStreamAndEvents db appendEventResult.NewStream appendEventResult.NewEvents appendEventResult
