module TinyEventStore.Queries

open System
open Microsoft.EntityFrameworkCore
open TinyEventStore
open TinyEventStore.Ef.Storables
open System.Linq
open FsToolkit.ErrorHandling

let loadStorableStream<'id, 'event, 'header when 'id: equality> (db: DbContext) (id: 'id) =
  task {
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

    return stream
  // return Ok(stream)
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
        let events = grouping |> Seq.toList // |> Seq.map Storable.toEvent |> Seq.toList

        let stream =
          { StreamId = streamId
            Events = events
            FromSequenceId = events.Head.SequenceId
            ToSequenceId = events.Last().SequenceId }

        if not (stream.ToSequenceId > stream.FromSequenceId) then
          failwith ("to sequence id <= From Sequence id")

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
