﻿module TinyEventStore.EfPure

open System
open Microsoft.EntityFrameworkCore
open Microsoft.Extensions.DependencyInjection
open TinyEventStore
open FsToolkit.ErrorHandling
open TinyEventStore.Ef.Storables

// USED
let genericLoadEvents<'id, 'event, 'header when 'id: equality> (db: DbContext) (id: 'id) =
  task {
    let! stream =
      db
        .Set<Stream<'id, 'event, 'header>>()
        .Include(fun x -> x.Events)
        .AsNoTracking()
        .SingleOrDefaultAsync(fun x -> x.Id = id)

    let stream: Stream<'id, 'event, 'header> =
      if box stream = null then
        let stream =
          { Stream.Id = id
            Version = 0u
            Created = DateTimeOffset.MinValue
            Modified = DateTimeOffset.MinValue
            Events = ResizeArray([]) }

        stream
      else
        stream

    return Result.Ok(stream)
  }

let loadStorableStream<'id, 'event, 'header when 'id: equality> (db: DbContext) (id: 'id) =
  task {
      let types = db.Model.GetEntityTypes()
    // try
      let! stream =
        db
          .Set<StorableStream<'id, 'event, 'header>>()
          // ORDER children by
          .Include(fun x -> x.Children)
          .AsNoTracking()
          .SingleOrDefaultAsync(fun x -> x.Id = id)

      let stream: Stream<'id, 'event, 'header> =
        if box stream = null then
          let stream =
            { Stream.Id = id
              Version = 0u
              // Created = DateTimeOffset.UtcNow
              Created = DateTimeOffset.MinValue
              Modified = DateTimeOffset.MinValue
              Events = ResizeArray([]) }

          stream
        else
          // stream
          let coreStream = Storable.toStream stream
          coreStream

      return Result.Ok(stream)
    // with e ->
    //   printfn "error %s %A" e.Message (db.GetType())
    //   db.Model.GetEntityTypes()|> Seq.iter (fun x -> printfn "entity %s" x.Name)
    //   return Result.Error e.Message
  }

open System.Linq

let loadMultipleStorableStream<'id, 'event, 'header when 'id: equality> (db: DbContext) (id: 'id list) =
  task {
    try
      let ids = ResizeArray(id)
      let! streams =
        db
          .Set<StorableStream<'id, 'event, 'header>>()
          // ORDER children by
          .Include(fun x -> x.Children)
          .AsNoTracking()
          .Where(fun x -> ids.Contains(x.Id))
          .ToListAsync()

      return Result.Ok(streams |> Seq.map Storable.toStream)
    with e ->
      printfn "error %s %A" e.Message (db.GetType())
      db.Model.GetEntityTypes()|> Seq.iter (fun x -> printfn "entity %s" x.Name)
      return Result.Error e.Message
  }

let loadAllStorableStream<'id, 'event, 'header when 'id: equality> (db: DbContext) =
  task {
    try
      let! streams =
        db
          .Set<StorableStream<'id, 'event, 'header>>()
          // ORDER children by
          .Include(fun x -> x.Children)
          .AsNoTracking()
          .ToListAsync()

      return Result.Ok(streams |> Seq.map Storable.toStream)
    with e ->
      printfn "error %s %A" e.Message (db.GetType())
      db.Model.GetEntityTypes()|> Seq.iter (fun x -> printfn "entity %s" x.Name)
      return Result.Error e.Message
  }

let efRehydrate<'id, 'state, 'event, 'header, 'Db when 'Db :> DbContext and 'id: equality>
  (zero: 'state)
  (evolve: Evolve<'id, 'state, 'event, 'header>)
  (ctx: IServiceProvider)
  (id: 'id)
  =
  let db = ctx.GetRequiredService<'Db>()
  let loadEvents = genericLoadEvents<'id, 'event, 'header> db

  taskResult {
    let! stream = loadEvents id
    let state = TinyEventStore.PureStore.rehydrate zero evolve stream
    return state, stream
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
    return streams |> Seq.map(fun stream -> (TinyEventStore.PureStore.rehydrate zero evolve stream),stream) |> Seq.toList
    // let state =
    // return state, stream
  }
let rehydrateAll<'id, 'state, 'event, 'header, 'Db when 'Db :> DbContext and 'id: equality>
  (zero: 'state)
  (evolve: Evolve<'id, 'state, 'event, 'header>)
  (ctx: IServiceProvider)
  =
  let db = ctx.GetRequiredService<'Db>()

  taskResult {
    let! streams = loadAllStorableStream<'id, 'event, 'header> db
    return streams |> Seq.map(fun stream -> (TinyEventStore.PureStore.rehydrate zero evolve stream),stream) |> Seq.toList
    // let state =
    // return state, stream
  }
// HERE IS THE Pure Stuff, is this really pure though? we are interacting with a database : /
let efAppendEvents<'id, 'state, 'event, 'header, 'Db when 'Db :> DbContext and 'id: equality>
  (zero: 'state)
  (evolve: Evolve<'id, 'state, 'event, 'header>)
  (ctx: IServiceProvider)
  (id: 'id)
  (events: ('event * 'header) list)
  =
  let db = ctx.GetRequiredService<'Db>()

  id
  |> genericLoadEvents<'id, 'event, 'header> db
  |> TaskResult.map (fun x -> TinyEventStore.PureStore.appendEvents zero evolve x (id, events))

let efCommandHandler<'id, 'state, 'command, 'ch, 'event, 'header, 'sideEffect, 'Db when 'Db :> DbContext and 'id: equality>
  zero
  (evolve: Evolve<'id, 'state, 'event, 'header>)
  (executeCommand: PureDecide<'id, 'state, 'command, 'ch, 'event, 'header, 'sideEffect>)
  (ctx: IServiceProvider)
  =
  let db = ctx.GetRequiredService<'Db>()
  let loadEvents = genericLoadEvents<'id, 'event, 'header> db

  fun (id: 'id) ->
    id
    |> loadEvents
    |> TaskResult.map (fun x -> TinyEventStore.PureStore.makeCommandHandler zero evolve executeCommand x)

let prepare<'id, 'state, 'command, 'ch, 'event, 'header, 'sideEffect, 'Db when 'Db :> DbContext and 'id: equality>
  zero
  (evolve: Evolve<'id, 'state, 'event, 'header>)
  (executeCommand: PureDecide<'id, 'state, 'command, 'ch, 'event, 'header, 'sideEffect>)
  (ctx: IServiceProvider)
  =
  let db = ctx.GetRequiredService<'Db>()
  let loadEvents = loadStorableStream<'id, 'event, 'header> db

  fun (id: 'id) ->
    id
    |> loadEvents
    |> TaskResult.map (fun x -> TinyEventStore.PureStore.makeCommandHandler zero evolve executeCommand x)
// maybe
type EfStore<'id, 'state, 'command, 'commandHeader, 'event, 'header, 'sideEffect, 'Db when 'Db :> DbContext> =
  { decide: IServiceProvider
      -> 'id
      -> TaskResult<CommandEnvelope<'id, 'command, 'commandHeader>
                      -> Result<CommandResult<'id, 'state, 'event, 'header, 'sideEffect>, string>, string>
    prepare: IServiceProvider
      -> 'id
      -> TaskResult<CommandEnvelope<'id, 'command, 'commandHeader>
                      -> Result<CommandResult<'id, 'state, 'event, 'header, 'sideEffect>, string>, string>
    append: IServiceProvider
      -> 'id
      -> ('event * 'header) list
      -> TaskResult<AppendEventsResult<'id, 'state, 'event, 'header>, string>
    rehydrateLatest: IServiceProvider -> 'id -> TaskResult<'state * Stream<'id, 'event, 'header>, string>
    rehydrateLatest2: IServiceProvider -> 'id -> TaskResult<'state * Stream<'id, 'event, 'header>, string>
    rehydrateMany: IServiceProvider -> 'id list -> TaskResult<('state * Stream<'id, 'event, 'header>) list, string>
    rehydrateAll: IServiceProvider -> TaskResult<('state * Stream<'id, 'event, 'header>) list, string>
    rehydrate: Stream<'id, 'event, 'header> -> 'state
    getDb: IServiceProvider -> 'Db
    updateEventStore: IServiceProvider -> OperationResult<'id, 'state, 'event, 'header> -> unit
    updateEventStore2: IServiceProvider -> OperationResult<'id, 'state, 'event, 'header> -> unit
  // updateDerivedstate: IServiceProvider -> OperationResult<'id, 'state, 'event, 'header> -> ('derived -> unit) -> unit
   }

[<RequireQualifiedAccess>]
type DbSideEffect =
  | Create
  | Update
  | Delete

let projectToDbCommand (events: EventEnvelope<'id, 'event, 'header> list) =
  if (events.Item 0).Version = 1u then
    DbSideEffect.Create
  else
    DbSideEffect.Update

let mapToDbOperation (db: DbContext) =
  function
  | DbSideEffect.Create -> db.Add >> ignore
  | DbSideEffect.Update -> db.Update >> ignore
  | DbSideEffect.Delete -> db.Remove >> ignore

let updateStreamAndEvents (db: DbContext) (stream: Stream<'id, 'event, 'header>) events =
  let dbCmd = projectToDbCommand events
  let insertOrUpdate x = mapToDbOperation db dbCmd x
  // stream is loaded beforehand, so we can use its entry

  let entry = db.Entry(stream)
  insertOrUpdate stream
  events |> List.iter (db.Add >> ignore)
  ()

let updateStorableStreamAndEvents (db: DbContext) (stream: Stream<'id, 'event, 'header>) events =
  let stream = Storable.toStorableStream stream
  let dbCmd = projectToDbCommand events
  let events = events |> List.map Storable.toStorableEvent
  let insertOrUpdate x = mapToDbOperation db dbCmd x
  // stream is loaded beforehand, so we can use its entry

  let entry = db.Entry(stream)
  insertOrUpdate stream
  events |> List.iter (db.Add >> ignore)
  ()

let updateDerived
  (db: DbContext)
  (commandResult: OperationResult<'Id, 'state, 'Event, 'EventHeader>)
  (derive: OperationResult<'Id, 'state, 'Event, 'EventHeader> -> 'derived)
  =
  let derived = derive commandResult
  let entry = db.Entry(derived)
  printfn "is key set%A " entry.IsKeySet
  entry.CurrentValues.Item"Id" <- commandResult.NewStream.Id
  printfn "is key set%A " entry.IsKeySet
  let dbCmd = projectToDbCommand commandResult.NewEvents
  let insertOrUpdate x = mapToDbOperation db dbCmd x
  insertOrUpdate derived
  ()

let updateEventStream (db: DbContext) (appendEventResult: OperationResult<'id, 'state, 'event, 'eventHeader>) =
  // updateStorableStreamAndEvents db appendEventResult.NewStream appendEventResult.NewEvents
  updateStreamAndEvents db appendEventResult.NewStream appendEventResult.NewEvents

let updateEventStream2 (db: DbContext) (appendEventResult: OperationResult<'id, 'state, 'event, 'eventHeader>) =
  updateStorableStreamAndEvents db appendEventResult.NewStream appendEventResult.NewEvents
// updateStreamAndEvents db appendEventResult.NewStream appendEventResult.NewEvents

let efCreate<'id, 'state, 'event, 'header, 'command, 'commandHeader, 'sideEffect, 'Db when 'Db :> DbContext and 'id: equality>
  (zero: 'state)
  (evolve: Evolve<'id, 'state, 'event, 'header>)
  (decide: PureDecide<'id, 'state, 'command, 'commandHeader, 'event, 'header, 'sideEffect>)
  : EfStore<'id, 'state, 'command, 'commandHeader, 'event, 'header, 'sideEffect, 'Db> =
  let rehydrateRaw = TinyEventStore.PureStore.rehydrate zero evolve

  let commandHandler =
    efCommandHandler<'id, 'state, 'command, 'commandHeader, 'event, 'header, 'sideEffect, 'Db> zero evolve decide

  let prepare =
    prepare<'id, 'state, 'command, 'commandHeader, 'event, 'header, 'sideEffect, 'Db> zero evolve decide

  let appendEventsHandler =
    efAppendEvents<'id, 'state, 'event, 'header, 'Db> zero evolve

  let rehydrateLatest = efRehydrate<'id, 'state, 'event, 'header, 'Db> zero evolve
  let rehydrateLatest2 = efRehydrate2<'id, 'state, 'event, 'header, 'Db> zero evolve
  let rehydrateMany = rehydrateMany<'id, 'state, 'event, 'header, 'Db> zero evolve
  let rehydrateAll = rehydrateAll<'id, 'state, 'event, 'header, 'Db> zero evolve

  { decide = commandHandler
    prepare = prepare
    append = appendEventsHandler
    rehydrateLatest = rehydrateLatest
    rehydrateLatest2 = rehydrateLatest2
    rehydrateMany = rehydrateMany
    rehydrateAll =  rehydrateAll
    rehydrate = TinyEventStore.PureStore.rehydrate zero evolve
    getDb = fun ctx -> ctx.GetService<'Db>()
    updateEventStore =
      fun ctx operationResult ->
        let db = ctx.GetService<'Db>()
        updateEventStream db operationResult
    updateEventStore2 =
      fun ctx operationResult ->
        let db = ctx.GetService<'Db>()
        updateEventStream2 db operationResult }
