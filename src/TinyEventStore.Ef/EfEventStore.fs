[<AutoOpen>]
module TinyEventStore.Ef

open System
open Microsoft.EntityFrameworkCore
open Microsoft.Extensions.DependencyInjection
open TinyEventStore
open FsToolkit.ErrorHandling
open Json
open TinyEventStore.EfUtils

let configureEventEnvelopeWithConversion<'id, 'rawId, 'event, 'eventDto, 'header, 'headerDto>
  (modelBuilder: ModelBuilder)
  (tableName: string)
  (idConverter: IdConverter<'id, 'rawId>)
  (payloadConverter: Converter<'event, 'eventDto>)
  (headerConverter: Converter<'header, 'headerDto>)
  =
  let entity = modelBuilder.Entity<EventEnvelope<'id, 'event, 'header>>()
  entity.HasKey(fun x -> x.EventId :> obj) |> ignore

  entity.ToTable tableName |> ignore

  entity
    .HasIndex(fun x -> (x.Version, x.StreamId) :> obj)
    .IsUnique()
    .IsDescending(false, false)
  |> ignore

  // probably wrong
  entity
    .Property(fun x -> x.CausationId)
    .HasConversion(serialize<CausationId option>, deserialize<CausationId option>)
  |> ignore

  let toRaw = Option.map CorrelationId.ToRawValue >> Option.toNullable

  let fromRaw = Option.ofNullable >> Option.map CorrelationId.FromRawValue

  entity.Property(fun x -> x.CorrelationId).HasConversion(toRaw, fromRaw)
  |> ignore

  entity
    .Property(fun x -> x.StreamId)
    .HasConversion(idConverter |> fst, idConverter |> snd)
  |> ignore

  let serializeEvent (e: 'event) =
    e |> (payloadConverter |> fst) |> serialize

  let deserializeEvent (dto: string) =
    dto |> deserialize<'eventDto> |> (payloadConverter |> snd)

  entity
    .Property(fun x -> x.Payload)
    .HasConversion(serializeEvent, deserializeEvent)
  |> ignore

  let serializeHeader (e: 'header) =
    e |> (headerConverter |> fst) |> serialize

  let deserializeHeader (dto: string) =
    dto |> deserialize<'headerDto> |> (headerConverter |> snd)

  entity
    .Property(fun x -> x.Header)
    .HasConversion(serializeHeader, deserializeHeader)
  |> ignore

  entity
    .Property(fun x -> x.EventId)
    .HasConversion(EventId.ToRawValue, EventId.FromRawValue)
  |> ignore

  entity

let configureEventEnvelope<'id, 'rawId, 'event, 'header>
  (modelBuilder: ModelBuilder)
  (tableName: string)
  (idConverter: IdConverter<'id, 'rawId>)
  =
  modelBuilder.Entity<EventEnvelope<'id, 'event, 'header>>(fun entity ->
    entity.HasKey(fun x -> x.EventId :> obj) |> ignore

    entity.ToTable tableName |> ignore

    entity
      .HasIndex(fun x -> (x.Version, x.StreamId) :> obj)
      .IsUnique()
      .IsDescending(false, false)
    |> ignore

    entity
      .Property(fun x -> x.CausationId)
      .HasConversion(serialize<CausationId option>, deserialize<CausationId option>)
    |> ignore

    let toRaw = Option.map CorrelationId.ToRawValue >> Option.toNullable

    let fromRaw = Option.ofNullable >> Option.map CorrelationId.FromRawValue

    entity.Property(fun x -> x.CorrelationId).HasConversion(toRaw, fromRaw)
    |> ignore

    entity
      .Property(fun x -> x.StreamId)
      .HasConversion(idConverter |> fst, idConverter |> snd)
    |> ignore

    entity
      .Property(fun x -> x.Payload)
      .HasConversion(serialize<'event>, deserialize<'event>)
    |> ignore

    entity
      .Property(fun x -> x.Header)
      .HasConversion(serialize<'header>, deserialize<'header>)
    |> ignore

    entity
      .Property(fun x -> x.EventId)
      .HasConversion(EventId.ToRawValue, EventId.FromRawValue)
    |> ignore

    ())
  |> ignore

let configureStream<'id, 'rawId, 'event, 'header>
  (modelBuilder: ModelBuilder)
  (converter: IdConverter<'id, 'rawId>)
  (tableName: string)
  =
  let entity = modelBuilder.Entity<Stream<'id, 'event, 'header>>()
  entity.HasKey(fun x -> x.Id :> obj) |> ignore
  entity.ToTable tableName |> ignore

  entity.Property(fun x -> x.Id).HasConversion(converter |> fst, converter |> snd)
  |> ignore

  entity

let configureEventStore<'id, 'rawId, 'event, 'header>
  (modelBuilder: ModelBuilder)
  (converter: IdConverter<'id, 'rawId>)
  (streamTableName: string)
  (eventTableName: string)
  =
  let streamEntityBuilder =
    configureStream<'id, 'rawId, 'event, 'header> modelBuilder converter streamTableName

  let eventEntityBuilder =
    configureEventEnvelopeWithConversion<'id, 'rawId, 'event, 'event, 'header, 'header>
      modelBuilder
      eventTableName
      converter
      (id, id)
      (id, id)

  streamEntityBuilder, eventEntityBuilder

let configureEventStoreWithConversions<'id, 'rawId, 'event, 'eventDto, 'header, 'headerDto>
  (modelBuilder: ModelBuilder)
  (converter: IdConverter<'id, 'rawId>)
  (eventConverter: IdConverter<'event, 'eventDto>)
  (headerConverter: IdConverter<'header, 'headerDto>)
  (streamTableName: string)
  (eventTableName: string)
  =
  configureStream<'id, 'rawId, 'event, 'header> modelBuilder converter streamTableName
  |> ignore

  configureEventEnvelopeWithConversion<'id, 'rawId, 'event, 'eventDto, 'header, 'headerDto>
    modelBuilder
    eventTableName
    converter
    eventConverter
    headerConverter
  |> ignore

let genericLoadEvents<'id, 'event, 'header when 'id: equality> (db: DbContext) (id: 'id) =
  task {
    let! stream =
      db
        .Set<Stream<'id, 'event, 'header>>()
        .Include(fun x -> x.Events)
        .AsNoTracking()
        .SingleOrDefaultAsync(fun x -> x.Id = id)

    let stream =
      if box stream = null then
        let stream =
          { Stream.Id = id
            Version = 0u
            Created = DateTimeOffset.UtcNow
            Events = ResizeArray [] }

        stream
      else
        stream

    return Result.Ok(stream)
  }

let genericCommit<'id, 'event, 'header, 'sideEffect>
  (db: DbContext)
  (events: EventEnvelope<'id, 'event, 'header> list, sideEffects: 'sideEffect list)
  =
  db.Set<EventEnvelope<'id, 'event, 'header>>().AddRange(events)
  Result.Ok()

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

let efAppendEvents<'id, 'state, 'event, 'header, 'Db when 'Db :> DbContext and 'id: equality>
  (zero: 'state)
  (evolve: Evolve<'id, 'state, 'event, 'header>)
  (ctx: IServiceProvider)
  (id: 'id)
  (events: ('event * 'header) list)
  =
  let db = ctx.GetRequiredService<'Db>()
  let loadEvents = genericLoadEvents<'id, 'event, 'header> db
  TinyEventStore.Store.appendEvents zero evolve loadEvents (id, events)

let efCommandHandler<'id, 'state, 'command, 'event, 'header, 'sideEffect, 'Db when 'Db :> DbContext and 'id: equality>
  zero
  (evolve: Evolve<'id, 'state, 'event, 'header>)
  (executeCommand: IServiceProvider -> Decide<'state, 'command, 'event, 'header, 'sideEffect>)
  (ctx: IServiceProvider)
  =
  let db = ctx.GetRequiredService<'Db>()
  let loadEvents = genericLoadEvents<'id, 'event, 'header>
  TinyEventStore.Store.makeCommandHandler zero evolve (executeCommand ctx) (loadEvents db)

type EfStore<'id, 'state, 'command, 'commandHeader, 'event, 'header, 'sideEffect, 'Db when 'Db :> DbContext> =
  { decide:
      IServiceProvider
        -> CommandEnvelope<'id, 'command,'commandHeader>
        -> TaskResult<CommandResult<'id, 'state, 'event, 'header, 'sideEffect>, string>
    append:
      IServiceProvider
        -> 'id
        -> ('event * 'header) list
        -> TaskResult<AppendEventsResult<'id, 'state, 'event, 'header>, string>
    rehydrateLatest: IServiceProvider -> 'id -> TaskResult<'state * Stream<'id, 'event, 'header>, string>
    rehydrate: Stream<'id, 'event, 'header> -> 'state }

let efCreate<'id, 'state, 'event, 'header, 'command, 'sideEffect, 'Db when 'Db :> DbContext and 'id: equality>
  (zero: 'state)
  (evolve: Evolve<'id, 'state, 'event, 'header>)
  (executeCommand: IServiceProvider -> Decide<'state, 'command, 'event, 'header, 'sideEffect>)
  : EfStore<'id, 'state, 'command,'commandHeader, 'event, 'header, 'sideEffect, 'Db> =

  let rehydrateRaw = TinyEventStore.PureStore.rehydrate zero evolve

  let commandHandler =
    efCommandHandler<'id, 'state, 'command, 'event, 'header, 'sideEffect, 'Db> zero evolve executeCommand

  let appendEventsHandler =
    efAppendEvents<'id, 'state, 'event, 'header, 'Db> zero evolve

  let rehydrateLatest = efRehydrate<'id, 'state, 'event, 'header, 'Db> zero evolve

  { decide = commandHandler
    append = appendEventsHandler
    rehydrateLatest = rehydrateLatest
    rehydrate = TinyEventStore.PureStore.rehydrate zero evolve }

