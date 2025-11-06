module TinyEventStore.Ef.Queries

open System
open Microsoft.EntityFrameworkCore
open TinyEventStore
open TinyEventStore.Ef.Storables
open System.Linq
open Microsoft.Extensions.DependencyInjection
open FsToolkit.ErrorHandling
open Core
open TinyEventStore.ApplyEvents

let queryStorableEvents<'id, 'event, 'header when 'id: equality> (db: DbContext) =
  db.Set<StorableEvent<'id, 'event, 'header>>().AsNoTracking()

let queryEvents<'id, 'event, 'header when 'id: equality> (db: DbContext) =
  (queryStorableEvents<'id, 'event, 'header> db).Select(Storable.toEvent)

let queryStorableStreams<'id, 'event, 'header when 'id: equality> (db: DbContext) =
  db.Set<StorableStream<'id, 'event, 'header>>().AsNoTracking()

let queryStreams<'id, 'event, 'header when 'id: equality> (db: DbContext) =
  (queryStorableStreams<'id, 'event, 'header> db).Select(Storable.toStream)

let loadStream (db: DbContext) (id, version) =
  match version with
  | None ->
    db
      .Set<StorableStream<'id, 'event, 'header>>()
      .Include(_.Children.OrderBy(_.Version))
      .AsNoTracking()
      .SingleOrDefaultAsync(fun x -> x.Id = id)
  | Some version ->
    db
      .Set<StorableStream<'id, 'event, 'header>>()
      .Include(_.Children.OrderBy(_.Version).Where(fun c -> c.Version <= version))
      .AsNoTracking()
      .SingleOrDefaultAsync(fun x -> x.Id = id)

let loadStorableStream<'id, 'event, 'header when 'id: equality> (db: DbContext) (id: 'id, version) =
  task {
    try
      let! stream = loadStream db (id, version)
      // db
      //   .Set<StorableStream<'id, 'event, 'header>>()
      //   // ORDER children by
      //   .Include(fun x -> x.Children)
      //   .AsNoTracking()
      //   .SingleOrDefaultAsync(fun x -> x.Id = id)

      let stream: Stream<'id, 'event, 'header> =
        if box stream = null then
          let stream =
            { Stream.Id = id
              Version = 0u
              IsDeleted = false
              Created = DateTimeOffset.MinValue
              Modified = DateTimeOffset.MinValue
              Events = ResizeArray([]) }

          stream
        else
          // stream
          let coreStream = Storable.toStream stream
          coreStream

      return Ok stream
    with e ->
      return Error(e.Message)
  }

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

      return Ok(streams |> Seq.map Storable.toStream)
    with e ->
      printfn "error %s %A" e.Message (db.GetType())

      db.Model.GetEntityTypes() |> Seq.iter (fun x -> printfn "entity %s" x.Name)

      return Error e.Message
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

      return Ok(streams |> Seq.map Storable.toStream)
    with e ->
      printfn "error %s %A" e.Message (db.GetType())

      db.Model.GetEntityTypes() |> Seq.iter (fun x -> printfn "entity %s" x.Name)

      return Error e.Message
  }

let loadEventsChunk<'state, 'id, 'event, 'header when 'id: equality>
  (db: DbContext)
  (from: uint32)
  (untilIncluding: uint32)
  =
  task {
    let! events =
      db
        .Set<StorableEvent<'id, 'event, 'header>>()
        .Include(fun x -> x.Stream)
        .Where(fun x -> x.Version >= from && x.Version <= untilIncluding)
        .OrderBy(fun v -> v.Version)
        .AsNoTracking()
        .ToListAsync()

    let streams = events.GroupBy(fun x -> x.StreamId)

    let streams2 =
      streams
      |> Seq.map (fun grouping ->
        let streamId = grouping.Key
        let events = grouping |> Seq.toList

        let stream =
          { StreamId = streamId
            IsDeleted = false
            Events = events
            FromSequenceId = events.Head.SequenceId
            ToSequenceId = events.Last().SequenceId }

        if stream.FromSequenceId > stream.ToSequenceId then
          failwith ($"From sequence id ({stream.FromSequenceId}) > To Sequence id ({stream.ToSequenceId})")

        stream)

    // let streams = events.GroupBy(fun x ->
    //   {
    //     StreamChunk.FromSequenceId = 0u
    //     ToSequenceId = 0u
    //     StreamId = x.StreamId
    //     StreamChunk = x.Stream
    //     }
    //   )
    return streams2
  }

let efRehydrate2<'id, 'state, 'event, 'header, 'Db when 'Db :> DbContext and 'id: equality>
  (zero: 'state)
  (evolve: Evolve<'id, 'state, 'event, 'header>)
  (ctx: IServiceProvider)
  (id: 'id)
  // : TaskResult<('state) * Stream<'id, 'event, 'header>, string>
  =
  let db = ctx.GetRequiredService<'Db>()
  let loadEvents = loadStorableStream<'id, 'event, 'header> db

  taskResult {
    let! stream = loadEvents (id, None)
    let state = rehydrate zero evolve stream
    return state, stream
  }

let efRehydrateAtVersion<'id, 'state, 'event, 'header, 'Db when 'Db :> DbContext and 'id: equality>
  (zero: 'state)
  (evolve: Evolve<'id, 'state, 'event, 'header>)
  (ctx: IServiceProvider)
  (id: 'id, version)
  // : TaskResult<('state) * Stream<'id, 'event, 'header>, string>
  =
  let db = ctx.GetRequiredService<'Db>()
  let loadEvents = loadStorableStream<'id, 'event, 'header> db

  taskResult {
    let! stream = loadEvents (id, Some version)
    let state = rehydrate zero evolve stream
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
      |> Seq.map (fun stream -> (rehydrate zero evolve stream), stream)
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
      |> Seq.map (fun stream -> (rehydrate zero evolve stream), stream)
      |> Seq.toList
  }
