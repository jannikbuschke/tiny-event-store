module TinyEventStore.Ef.Projections

open System
open Microsoft.EntityFrameworkCore
open Microsoft.Extensions.DependencyInjection
open TinyEventStore
open TinyEventStore.Ef.Storables
open Queries
open Core
open TinyEventStore.ApplyEvents

type IEfProjection<'id, 'state, 'event, 'header, 'Db when 'Db :> DbContext> =
  abstract member Apply: IServiceProvider -> OperationResult<'id, 'state, 'event, 'header> -> unit

type EfProjection<'id, 'state, 'event, 'header, 'a, 'Db when 'Db :> DbContext and 'a: not struct>
  (f: OperationResult<'id, 'state, 'event, 'header> -> 'a) =
  interface IEfProjection<'id, 'state, 'event, 'header, 'Db> with
    member _.Apply ctx op =
      let db = ctx.GetRequiredService<'Db>()
      let dbOp0 = op |> getDefaultDbOperation
      let dbOp = dbOp0 |> mapToEfContextOperation db
      let a = op |> f
      a |> dbOp

let applyEvents
  (memory: Collections.Generic.Dictionary<'id, 'state * Stream<'id, 'event, 'header>>)
  originalAggregate
  streamChunk
  =
  let streamId = streamChunk.StreamId

  let (state, stream) =
    if memory.ContainsKey streamId then
      memory.Item streamId
    else
      let stream =
        { Stream.Version = 0u
          Id = streamId
          Created = DateTimeOffset.MinValue
          Modified = DateTimeOffset.MinValue
          IsDeleted = false
          Events = [||] }

      originalAggregate.zero, stream

  let newEvents = (streamChunk.Events |> List.map Storable.toEvent)

  let result =
    applyEvents originalAggregate state stream newEvents :> OperationResult<'id, 'state, 'event, 'header>

  result

let rerunProjection<'state, 'id, 'event, 'header when 'id: equality>
  (memory: Collections.Generic.Dictionary<'id, 'state * Stream<'id, 'event, 'header>>)
  (originalAggregate: Aggregate<'id, 'state, 'event, 'header>)
  (db: DbContext)
  (fromSequence: uint32)
  (untilIncludingSequence: uint32)
  =
  task {
    let! streamChunks = loadEventsChunk<'state, 'id, 'event, 'header> db fromSequence untilIncludingSequence
    return streamChunks |> Seq.map (applyEvents memory originalAggregate)
  }

let rerunProject<'id, 'state, 'event, 'header, 'db when 'id: equality and 'db :> DbContext>
  (originalAggregate: Aggregate<'id, 'state, 'event, 'header>)
  (services: IServiceProvider)
  (projection: IEfProjection<'id, 'state, 'event, 'header, 'db>)
  =

  let memory =
    Collections.Generic.Dictionary<'id, 'state * Stream<'id, 'event, 'header>>()

  task {
    let mutable running = true
    let mutable version = 0u

    while running do
      let fromSequence = version + 1u
      let untilIncludingSequence = version + 10u
      use scope = services.CreateScope()
      let db = scope.ServiceProvider.GetRequiredService<'db>()
      // printfn "Rerunning %d -> %d" fromSequence untilIncludingSequence

      let! streamsAndState =
        rerunProjection<'state, 'id, 'event, 'header> memory originalAggregate db fromSequence untilIncludingSequence

      streamsAndState
      |> Seq.iter (fun (result) ->
        projection.Apply scope.ServiceProvider result
        // printfn "Stream chunk %A events = %A" result.NewStream.Id result.NewEvents.Length
        // apply events to db and call save changes
        memory.[result.NewStream.Id] <- (result.New.State, result.New.Stream)
        ())

      let! x = db.SaveChangesAsync()

      if streamsAndState |> Seq.length = 0 then
        running <- false

      // printfn "saved %d changes while rerunning prohection (%d->%d)" x fromSequence untilIncludingSequence
      version <- untilIncludingSequence

  // let result =
  //   memory.Values
  //   |> Seq.map (fun (state, stream) -> state, (stream |> Storable.chunkToStream))
  //
  // printfn "Result after replaying projection %A" result
  // return result |> Seq.toList
  }


let updateDerivedWithDbOperation
  (db: DbContext)
  (commandResult: OperationResult<'Id, 'state, 'Event, 'EventHeader>)
  (derive: OperationResult<'Id, 'state, 'Event, 'EventHeader> -> 'derived * DbSideEffect)
  =
  let derived, dbCmd = derive commandResult
  let entry = db.Entry(derived)
  // this is not explicit, should be refactored mayb
  entry.CurrentValues.Item "Id" <- commandResult.NewStream.Id
  let insertOrUpdate x = mapToEfContextOperation db dbCmd x
  insertOrUpdate derived
  ()

let updateDerived
  (db: DbContext)
  (commandResult: OperationResult<'Id, 'state, 'Event, 'EventHeader>)
  (derive: OperationResult<'Id, 'state, 'Event, 'EventHeader> -> 'derived)
  =
  let derived = derive commandResult
  let entry = db.Entry(derived)
  // this is not explicit, should be refactored mayb
  entry.CurrentValues.Item "Id" <- commandResult.NewStream.Id
  // let dbCmd = projectToDbCommand commandResult.NewEvents
  let dbCmd = getDefaultDbOperation commandResult
  let insertOrUpdate x = mapToEfContextOperation db dbCmd x
  insertOrUpdate derived
  ()
// ()
