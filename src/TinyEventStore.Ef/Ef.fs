module TinyEventStore.EfEs

open System
open System.Threading.Tasks
open Microsoft.EntityFrameworkCore
open Microsoft.Extensions.DependencyInjection
open TinyEventStore
open FsToolkit.ErrorHandling
open TinyEventStore.Ef.Storables
open Queries
open EfUtils
// Todo
// do a little more cleanup
// and implement projection replay (189)


let efAppendEvents<'id, 'state, 'event, 'header, 'Db when 'Db :> DbContext and 'id: equality>
  (aggregate: Aggregate<'id, 'state, 'event, 'header>)
  (ctx: IServiceProvider)
  (id: 'id)
  (events: ('event * 'header) list)
  =
  let db = ctx.GetRequiredService<'Db>()

  taskResult {
    let! stream = loadStorableStream<'id, 'event, 'header> db id

    let result =
      TinyEventStore.PureStore.appendEvents aggregate stream (id, events)
      :> OperationResult<'id, 'state, 'event, 'header>

    return result
  }

let rerunProjection<'state, 'id, 'event, 'header when 'id: equality>
  (memory: System.Collections.Generic.Dictionary<'id, 'state * StreamChunk<'id, 'event, 'header>>)
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
        // let y = grouping |> Seq.map(fun x -> ())
        // let newChunk = grouping.Key

        // let stream = grouping.Key |> Storable.toStream
        let state =
          TinyEventStore.PureStore.rehydrateEvents
            existingState
            evolve
            (streamChunk.Events |> Seq.map Storable.toEvent)

        let combinedChunk = streamChunk |> StreamChunk.Append existingChunk
        // let combinedChunk = newChunk
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

let efRehydrate2<'id, 'state, 'event, 'header, 'Db when 'Db :> DbContext and 'id: equality>
  (zero: 'state)
  (evolve: Evolve<'id, 'state, 'event, 'header>)
  (ctx: IServiceProvider)
  (id: 'id)
  =
  let db = ctx.GetRequiredService<'Db>()
  let loadEvents = loadStorableStream<'id, 'event, 'header> db

  taskResult {
    let! stream = loadEvents id
    let state = TinyEventStore.PureStore.rehydrate zero evolve stream
    return state, stream
  }

let rehydrateMany<'id, 'state, 'event, 'header, 'Db when 'Db :> DbContext and 'id: equality>
  (zero: 'state)
  (evolve: Evolve<'id, 'state, 'event, 'header>)
  (ctx: IServiceProvider)
  (id: 'id list)
  =
  let db = ctx.GetRequiredService<'Db>()
  let loadEvents = loadMultipleStorableStream<'id, 'event, 'header> db

  taskResult {
    let! streams = loadEvents id

    return
      streams
      |> Seq.map (fun stream -> (TinyEventStore.PureStore.rehydrate zero evolve stream), stream)
      |> Seq.toList
  }

let rehydrateAll<'id, 'state, 'event, 'header, 'Db when 'Db :> DbContext and 'id: equality>
  (zero: 'state)
  (evolve: Evolve<'id, 'state, 'event, 'header>)
  (ctx: IServiceProvider)
  =
  let db = ctx.GetRequiredService<'Db>()

  taskResult {
    let! streams = loadAllStorableStream<'id, 'event, 'header> db

    return
      streams
      |> Seq.map (fun stream -> (PureStore.rehydrate zero evolve stream), stream)
      |> Seq.toList
  }

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
    |> Task.map (fun x -> PureStore.makeCommandHandler aggregate executeCommand x)


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

type EfStore<'id, 'state, 'command, 'commandHeader, 'event, 'header, 'sideEffect, 'Db when 'Db :> DbContext> =
  { prepare:
      IServiceProvider
        -> 'id
        -> Task<
          CommandEnvelope<'id, 'command, 'commandHeader>
            -> Result<CommandResult<'id, 'state, 'event, 'header, 'sideEffect>, string>
         >
    // projections: IProjection list
    appendEvents:
      IServiceProvider
        -> 'id
        -> ('event * 'header) list
        -> TaskResult<OperationResult<'id, 'state, 'event, 'header>, string>
    rerunProject: IServiceProvider -> Task<('state * Stream<'id, 'event, 'header>) list>
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

  }

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

let updateEventStream2 (db: DbContext) (appendEventResult: OperationResult<'id, 'state, 'event, 'eventHeader>) =
  updateStorableStreamAndEvents db appendEventResult.NewStream appendEventResult.NewEvents

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
  let rerunProjection = rerunProject<'id, 'state, 'event, 'header, 'Db> zero evolve

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
    rehydrate = TinyEventStore.PureStore.rehydrate zero evolve
    getDb = fun ctx -> ctx.GetService<'Db>()
    applyOperationResultToProjections = fun _ _ -> ()
    appendEvents = appendEvents
    updateEventStore2 = updateEventStore2
    rerunProject = rerunProjection
    replayProjection = fun _ _ -> failwith "Not implemented"
    applyCommand = fun _ _ -> failwith "Not Implemented1"
    applyEvents = fun _ _ -> failwith "Not Implemented2"
    saveChangesAsync = fun _ -> failwith "Not Implemented3" }



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
    let rerunProjection = rerunProject<'id, 'state, 'event, 'header, 'Db> zero evolve

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
      rerunProject = rerunProjection
      replayProjection = fun _ _ -> failwith "Not implemented"
      saveChangesAsync =
        fun (ctx) ->
          taskResult {
            let db = ctx.GetService<'Db>()
            let! _ = db.SaveChangesAsync()
            return ()
          } }
