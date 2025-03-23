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
open System.Linq
open TinyEventStore.ApplyEvents
open Storables

type Store<'id, 'state, 'command, 'commandHeader, 'event, 'header, 'sideEffect, 'Db
  when 'id: equality and 'Db :> DbContext> =
  { StreamSet: DbSet<StorableStream<'id, 'event, 'header>>
    EventSet: DbSet<StorableEvent<'id, 'event, 'header>>
    prepare:
      'id
        -> TaskResult<
          CommandEnvelope<'id, 'command, 'commandHeader>
            -> Result<CommandResult<'id, 'state, 'event, 'header, 'sideEffect>, string>,
          string
         >
    appendEvents: 'id -> ('event * 'header) list -> TaskResult<OperationResult<'id, 'state, 'event, 'header>, string>
    replayProjection: IEfProjection<'id, 'state, 'event, 'header, 'Db> -> TaskResult<unit, string>
    rehydrateLatest2: 'id -> TaskResult<'state * Stream<'id, 'event, 'header>, string>
    rehydrate: 'id -> TaskResult<'state * Stream<'id, 'event, 'header>, string>
    rehydrateAtVersion: ('id * uint64) -> TaskResult<'state * Stream<'id, 'event, 'header>, string>
    rehydrateMany: 'id list -> TaskResult<('state * Stream<'id, 'event, 'header>) list, string>
    rehydrateAll: TaskResult<('state * Stream<'id, 'event, 'header>) list, string>
    getDb: 'Db
    updateEventStore2: OperationResult<'id, 'state, 'event, 'header> -> unit
    applyOperationResultToProjections: OperationResult<'id, 'state, 'event, 'header> -> unit
    applyCommand:
      ('id * CommandEnvelope<'id, 'command, 'commandHeader>)
        -> TaskResult<CommandResult<'id, 'state, 'event, 'header, 'sideEffect>, string>
    applyEvents: 'id * ('event * 'header) list -> TaskResult<OperationResult<'id, 'state, 'event, 'header>, string>
    aggregate: Aggregate<'id, 'state, 'event, 'header>
    saveChangesAsync: TaskResult<unit, string>
    saveChangesAsyncWithResult: TaskResult<int, string>
    queryStreams: IQueryable<Stream<'id, 'event, 'header>>
    queryRawStreams: IQueryable<Storables.StorableStream<'id, 'event, 'header>>
    queryEvents: IQueryable<EventEnvelope<'id, 'event, 'header>>
    queryRawEvents: IQueryable<Storables.StorableEvent<'id, 'event, 'header>> }

type EfStore<'id, 'state, 'command, 'commandHeader, 'event, 'header, 'sideEffect, 'Db
  when 'id: equality and 'Db :> DbContext> =
  { StreamSet: IServiceProvider -> DbSet<StorableStream<'id, 'event, 'header>>
    EventSet: IServiceProvider -> DbSet<StorableEvent<'id, 'event, 'header>>
    prepare:
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
    rehydrate: IServiceProvider -> 'id -> TaskResult<'state * Stream<'id, 'event, 'header>, string>
    rehydrateAtVersion: IServiceProvider -> ('id * uint64) -> TaskResult<'state * Stream<'id, 'event, 'header>, string>
    rehydrateMany: IServiceProvider -> 'id list -> TaskResult<('state * Stream<'id, 'event, 'header>) list, string>
    rehydrateAll: IServiceProvider -> TaskResult<('state * Stream<'id, 'event, 'header>) list, string>
    // rehydrate: Stream<'id, 'event, 'header> -> 'state
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
    saveChangesAsyncWithResult: IServiceProvider -> TaskResult<int, string>
    queryStreams: IServiceProvider -> IQueryable<Stream<'id, 'event, 'header>>
    queryRawStreams: IServiceProvider -> IQueryable<Storables.StorableStream<'id, 'event, 'header>>
    queryEvents: IServiceProvider -> IQueryable<EventEnvelope<'id, 'event, 'header>>
    queryRawEvents: IServiceProvider -> IQueryable<Storables.StorableEvent<'id, 'event, 'header>> }

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
    let replayProjection = rerunProject<'id, 'state, 'event, 'header, 'Db> aggregate

    let queryStreams = Queries.queryStreams<'id, 'event, 'header>
    let queryRawStreams = Queries.queryStorableStreams<'id, 'event, 'header>

    let queryEvents = Queries.queryEvents<'id, 'event, 'header>
    let queryRawEvents = Queries.queryStorableEvents<'id, 'event, 'header>

    let getDb (ctx: IServiceProvider) = ctx.GetService<'Db>()

    let applyResultToProjections serviceProvider result =
      projections |> List.iter (fun p -> p.Apply serviceProvider result)

    let updateEventStore2 (ctx: IServiceProvider) operationResult =
      let db = getDb ctx
      updateEventStream2 db operationResult

    let applyEvents (ctx: IServiceProvider) (streamId, events) =
      taskResult {
        // assert (events |> Seq.length > 0)

        if events |> Seq.length = 0 then
          failwith "no events given"

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

    { StreamSet =
        fun ctx ->
          let db = getDb ctx
          db.Set<StorableStream<'id, 'event, 'header>>()
      EventSet =
        fun ctx ->
          let db = getDb ctx
          db.Set<StorableEvent<'id, 'event, 'header>>()
      prepare = prepare
      applyOperationResultToProjections = applyResultToProjections
      aggregate = aggregate
      rehydrateLatest2 = rehydrateLatest2
      rehydrate = rehydrateLatest2
      rehydrateAtVersion = failwith ""
      rehydrateMany = rehydrateMany
      rehydrateAll = rehydrateAll
      // rehydrate = rehydrate zero evolve
      getDb = fun ctx -> getDb ctx
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
            let db = getDb ctx
            let! r = db.SaveChangesAsync()
            return r
          }
      saveChangesAsync =
        fun (ctx) ->
          taskResult {
            let db = getDb ctx
            let! _ = db.SaveChangesAsync()
            return ()
          }
      queryRawEvents =
        fun ctx ->
          let db = ctx.GetService<'Db>()
          queryRawEvents db
      queryEvents =
        fun ctx ->
          let db = ctx.GetService<'Db>()
          queryEvents db
      queryRawStreams =
        fun ctx ->
          let db = ctx.GetService<'Db>()
          queryRawStreams db
      queryStreams =
        fun ctx ->
          let db = ctx.GetService<'Db>()
          queryStreams db

    }
