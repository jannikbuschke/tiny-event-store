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
open Storables

type Subscription<'id,'event,'header when 'id : equality> = (IServiceProvider -> StorableEvent<'id, 'event, 'header> -> unit )
type EfStore<'id, 'state, 'command, 'commandHeader, 'event, 'header, 'sideEffect, 'Db
  when 'id: equality and 'Db :> DbContext> =
  { StreamSet: IServiceProvider -> DbSet<StorableStream<'id, 'event, 'header>>
    EventSet: IServiceProvider -> DbSet<StorableEvent<'id, 'event, 'header>>
    // getStore: IServiceProvider -> Store<'id, 'state, 'command, 'commandHeader, 'event, 'header, 'sideEffect, 'Db>
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
    rehydrateAtVersion: IServiceProvider -> ('id * uint32) -> TaskResult<'state * Stream<'id, 'event, 'header>, string>
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
    applyEvents2:
      IServiceProvider
        -> 'id * ('event * 'header * CausationId option) list
        -> TaskResult<OperationResult<'id, 'state, 'event, 'header>, string>
    aggregate: Aggregate<'id, 'state, 'event, 'header>
    saveChangesAsync: IServiceProvider -> TaskResult<unit, string>
    saveChangesAsyncWithResult: IServiceProvider -> TaskResult<int, string>
    subscribe: Subscription<'id, 'event,'header> -> unit
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
    let subscribers = ResizeArray<Subscription<'id,'event,'header>>()

    let prepare =
      prepare<'id, 'state, 'command, 'commandHeader, 'event, 'header, unit, 'Db> aggregate decide

    let rehydrateLatest2 = efRehydrate2<'id, 'state, 'event, 'header, 'Db> zero evolve
    let rehydrate = efRehydrateAtVersion<'id, 'state, 'event, 'header, 'Db> zero evolve
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
    let saveChangesAsyncWithResult(ctx)=
          taskResult {
            let db = getDb ctx
            // let newObjects = db.ChangeTracker
            //                    .Entries()
            //                   .Where(fun x -> x.State=EntityState.Added)
            //                   .ToList()
            let newEvents = db.ChangeTracker
                              .Entries<StorableEvent<'id,'event,'header>>()
        //                       .OfType<EventEnvelope<'id, 'event, 'header>>()
                              .Where(fun x -> x.State=EntityState.Added)
                              // .Where(fun x -> x.
                              // .OfType<EventEnvelope<'id, 'event, 'header>>()
                              .ToList()


            printfn "Save changes Events: %A" newEvents
            printfn "subs %A" subscribers
            // printfn "Save changes  neobjects: %A" newObjects
            let! r = db.SaveChangesAsync()
            subscribers |> Seq.iter(fun s ->
              newEvents |> Seq.iter(fun e ->
              s ctx e.Entity
                )
              )
            return r
          }
    let saveChangesAsync ctx =
       saveChangesAsyncWithResult ctx |> TaskResult.map ignore

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
      rehydrateAtVersion = rehydrate
      rehydrateMany = rehydrateMany
      rehydrateAll = rehydrateAll
      // rehydrate = rehydrate zero evolve
      getDb = fun ctx -> getDb ctx
      appendEvents = fun ctx id events -> appendEvents ctx id (events |> List.map (fun (e, h) -> e, h, None))
      applyCommand = applyCommand
      applyEvents = fun ctx (id, events) -> applyEvents ctx (id, (events |> List.map (fun (e, h) -> e, h, None)))
      applyEvents2 = applyEvents
      updateEventStore2 = updateEventStore2
      replayProjection =
        fun ctx projection ->
          taskResult {
            let! result = replayProjection ctx projection
            return ()
          }
      saveChangesAsyncWithResult = saveChangesAsyncWithResult
      saveChangesAsync = saveChangesAsync
      subscribe = fun fn ->
        printfn "subscribing %A" fn
        subscribers.Add(fn)
        ()
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
          queryStreams db }

type Store<'id, 'state, 'command, 'commandHeader, 'event, 'header, 'sideEffect, 'Db
  when 'id: equality and 'Db :> DbContext>
  (
    serviceProvider: IServiceProvider,
    store: EfStore<'id, 'state, 'command, 'commandHeader, 'event, 'header, 'sideEffect, 'Db>
  ) =

  member this.StreamSet = store.StreamSet serviceProvider
  member this.EventSet = store.EventSet serviceProvider
  member this.prepare = store.prepare serviceProvider
  member this.appendEvents = store.appendEvents serviceProvider
  member this.replayProjection = store.replayProjection serviceProvider
  member this.rehydrateLatest2 = store.rehydrateLatest2 serviceProvider
  member this.rehydrate = store.rehydrate serviceProvider
  member this.rehydrateAtVersion = store.rehydrateAtVersion serviceProvider
  member this.rehydrateMany = store.rehydrateMany serviceProvider
  member this.rehydrateAll = store.rehydrateAll serviceProvider
  member this.getDb = store.getDb serviceProvider
  member this.updateEventStore2 = store.updateEventStore2 serviceProvider

  member this.applyOperationResultToProjections =
    store.applyOperationResultToProjections serviceProvider

  member this.applyCommand = store.applyCommand serviceProvider
  member this.applyEvents = store.applyEvents serviceProvider
  member this.aggregate = store.aggregate
  member this.saveChangesAsync = store.saveChangesAsync serviceProvider

  member this.saveChangesAsyncWithResult =
    store.saveChangesAsyncWithResult serviceProvider

  member this.queryStreams = store.queryStreams serviceProvider
  member this.queryRawStreams = store.queryRawStreams serviceProvider
  member this.queryEvents = store.queryEvents serviceProvider
  member this.queryRawEvents = store.queryRawEvents serviceProvider
// }
