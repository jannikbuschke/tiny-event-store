module TinyEventStore.Ef.Projections

open System
open System.Threading.Tasks
open Microsoft.EntityFrameworkCore
open Microsoft.Extensions.DependencyInjection
open TinyEventStore
open FsToolkit.ErrorHandling
open TinyEventStore.Ef.Storables
open Queries
open Core

type IEfProjection<'id, 'state, 'event, 'header, 'Db when 'Db :> DbContext> =
  abstract member Apply: IServiceProvider -> OperationResult<'id, 'state, 'event, 'header> -> unit

type EfProjection<'id, 'state, 'event, 'header, 'a, 'Db when 'Db :> DbContext and 'a: not struct>
  (f: OperationResult<'id, 'state, 'event, 'header> -> 'a) =
  interface IEfProjection<'id, 'state, 'event, 'header, 'Db> with
    member _.Apply (ctx) (op) =
      let db = ctx.GetRequiredService<'Db>()
      let dbOp0 = op |> getDefaultDbOperation
      let dbOp = dbOp0 |> mapToEfContextOperation db
      let a = op |> f
      a |> dbOp

let rerunProjection<'state, 'id, 'event, 'header when 'id: equality>
  (memory: Collections.Generic.Dictionary<'id, 'state * StreamChunk<'id, 'event, 'header>>)
  (zero: 'state)
  (evolve: Evolve<'id, 'state, 'event, 'header>)
  (db: DbContext)
  (fromSequence: uint32)
  (untilIncludingSequence: uint32)
  =
  task {
    let! streamChunks = loadEventsChunk<'state, 'id, 'event, 'header> db fromSequence (untilIncludingSequence: uint32)

    let statesAndStreams =
      streamChunks
      |> Seq.map (fun streamChunk ->
        let streamId = streamChunk.StreamId

        let existingState, existingChunk =
          if memory.ContainsKey streamId then
            memory.Item streamId
          else
            zero, StreamChunk<'id, 'event, 'header>.Zero

        let state =
          PureStore.rehydrateEvents existingState evolve (streamChunk.Events |> Seq.map Storable.toEvent)

        let combinedChunk = streamChunk |> StreamChunk.Append existingChunk
        state, combinedChunk)

    return statesAndStreams
  }

let rerunProject<'id, 'state, 'event, 'header, 'db when 'id: equality and 'db :> DbContext>
  (zero: 'state)
  (evolve: Evolve<'id, 'state, 'event, 'header>)
  (services: IServiceProvider)
  =
  let db = services.GetService<'db>()

  let memory =
    Collections.Generic.Dictionary<'id, 'state * StreamChunk<'id, 'event, 'header>>()

  task {
    let mutable running = true
    // 1...500
    // 501...1000
    let mutable version = 0u

    while running do
      let fromSequence = version + 1u
      let untilIncludingSequence = version + 10u

      let! streamsAndState =
        rerunProjection<'state, 'id, 'event, 'header> memory zero evolve db fromSequence untilIncludingSequence

      streamsAndState
      |> Seq.iter (fun (state, stream) ->
        memory.[stream.StreamId] <- (state, stream)
        ())

      version <- untilIncludingSequence

      if true then
        running <- false

    let result =
      memory.Values
      |> Seq.map (fun (state, stream) -> state, (stream |> Storable.chunkToStream))

    printfn "result %A" result
    return result |> Seq.toList
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
  let dbCmd = projectToDbCommand commandResult.NewEvents
  let insertOrUpdate x = mapToEfContextOperation db dbCmd x
  insertOrUpdate derived
  ()
