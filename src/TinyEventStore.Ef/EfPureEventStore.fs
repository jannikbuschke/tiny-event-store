module TinyEventStore.EfPure

open System
open System.Runtime.CompilerServices
open Microsoft.EntityFrameworkCore
open System.Text.Json
open System.Text.Json.Serialization
open Microsoft.Extensions.DependencyInjection
open TinyEventStore
open FsToolkit.ErrorHandling
open TinyEventStore.EfUtils
open Json

let configureEventEnvelopeWithConversion<'id, 'rawId, 'event, 'eventDto, 'header, 'headerDto>
  (modelBuilder: ModelBuilder)
  (tableName: string)
  (idConverter: IdConverter<'id, 'rawId>)
  (payloadConverter: Converter<'event, 'eventDto>)
  (headerConverter: Converter<'header, 'headerDto>)
  =
  modelBuilder.Entity<EventEnvelope<'id, 'event, 'header>>(fun entity ->
    entity.HasKey(fun x -> x.EventId :> obj) |> ignore

    entity.ToTable tableName |> ignore

    entity
      .HasIndex(fun x -> (x.Version, x.StreamId) :> obj)
      .IsUnique()
      .IsDescending(false, false)
    |> ignore

    // maybe split this into two columns, one column "CausationType": string option, and a second column "CausationValue": guid option
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

    ())
  |> ignore

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
  modelBuilder.Entity<Stream<'id, 'event, 'header>>(fun entity ->
    entity.HasKey(fun x -> x.Id :> obj) |> ignore

    entity.ToTable tableName |> ignore

    entity.Property(fun x -> x.Id).HasConversion(converter |> fst, converter |> snd)
    |> ignore)
  |> ignore

let configureEventStore<'id, 'rawId, 'event, 'header>
  (modelBuilder: ModelBuilder)
  (converter: IdConverter<'id, 'rawId>)
  (streamTableName: string)
  (eventTableName: string)
  =
  configureStream<'id, 'rawId, 'event, 'header> modelBuilder converter streamTableName

  configureEventEnvelopeWithConversion<'id, 'rawId, 'event, 'event, 'header, 'header>
    modelBuilder
    eventTableName
    converter
    (id, id)
    (id, id)

let configureEventStoreWithConversions<'id, 'rawId, 'event, 'eventDto, 'header, 'headerDto>
  (modelBuilder: ModelBuilder)
  (converter: IdConverter<'id, 'rawId>)
  (eventConverter: IdConverter<'event, 'eventDto>)
  (headerConverter: IdConverter<'header, 'headerDto>)
  (streamTableName: string)
  (eventTableName: string)
  =
  configureStream<'id, 'rawId, 'event, 'header> modelBuilder converter streamTableName

  configureEventEnvelopeWithConversion<'id, 'rawId, 'event, 'eventDto, 'header, 'headerDto>
    modelBuilder
    eventTableName
    converter
    eventConverter
    headerConverter

[<Extension>]
type ModelBuilderExtensions() =

  [<Extension>]
  static member AddEventStore<'id, 'rawId, 'event, 'header>
    (ty: ModelBuilder, converter: IdConverter<'id, 'rawId>, entityName: string)
    =
    configureEventStore<'id, 'rawId, 'event, 'header>
      ty
      converter
      (sprintf "%s.Streams" entityName)
      (sprintf "%s.Events" entityName)

    ()

  [<Extension>]
  static member AddEventStore<'id, 'rawId, 'event, 'eventDto, 'header, 'headerDto>
    (
      ty: ModelBuilder,
      converter: IdConverter<'id, 'rawId>,
      eventConverter: IdConverter<'event, 'eventDto>,
      headerConverter: IdConverter<'header, 'headerDto>,
      entityName: string
    ) =
    configureEventStoreWithConversions<'id, 'rawId, 'event, 'eventDto, 'header, 'headerDto>
      ty
      converter
      eventConverter
      headerConverter
      (sprintf "%s.Streams" entityName)
      (sprintf "%s.Events" entityName)

    ()

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
            Events = System.Collections.Generic.List([]) }

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
  // let sideEffects =
  //   sideEffects
  //   |> List.map (fun s ->
  //     { SideEffectEnvelope.SideEffectId = SideEffectId.New()
  //       Data = s
  //       CreatedAt = DateTimeOffset.Now
  //       HandledAt = None
  //       Error = None })
  // TODO
  // db.Outbox.AddRange(sideEffects)
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

let efCommandHandler<'id, 'state, 'command, 'event, 'header, 'sideEffect, 'Db when 'Db :> DbContext and 'id: equality>
  zero
  (evolve: Evolve<'id, 'state, 'event, 'header>)
  (executeCommand: PureDecide<'id, 'state, 'command, 'event, 'header, 'sideEffect>)
  (ctx: IServiceProvider)
  =
  let db = ctx.GetRequiredService<'Db>()
  let loadEvents = genericLoadEvents<'id, 'event, 'header> db

  fun (id: 'id) ->
    id
    |> loadEvents
    |> TaskResult.map (fun x -> TinyEventStore.PureStore.makeCommandHandler zero evolve executeCommand x)


// maybe
type EfStore<'id, 'state, 'command, 'event, 'header, 'sideEffect, 'Db when 'Db :> DbContext> =
  { decide:
      IServiceProvider
        -> 'id
        -> TaskResult<
          CommandEnvelope<'id, 'command> -> Result<CommandResult<'id, 'state, 'event, 'header, 'sideEffect>, string>,
          string
         >
    append:
      IServiceProvider
        -> 'id
        -> ('event * 'header) list
        -> TaskResult<AppendEventsResult<'id, 'state, 'event, 'header>, string>
    rehydrateLatest: IServiceProvider -> 'id -> TaskResult<'state * Stream<'id, 'event, 'header>, string>
    rehydrate: Stream<'id, 'event, 'header> -> 'state
    getDb: IServiceProvider -> 'Db
    updateEventStore: IServiceProvider -> OperationResult<'id, 'state, 'event, 'header> -> unit
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

// let updateStreamAndEvents2 (db: DbContext) (commandResult: CommandResult<'Id, 'state, 'Event, 'EventHeader, 'SideEffect>) =
//   let state, stream, events, _, _ = commandResult
//   updateStreamAndEvents db stream events

let updateDerived
  (db: DbContext)
  (commandResult: OperationResult<'Id, 'state, 'Event, 'EventHeader>)
  (derive: OperationResult<'Id, 'state, 'Event, 'EventHeader> -> 'derived)
  =
  // let state, stream, events, _, _ = commandResult
  let derived = derive commandResult
  let dbCmd = projectToDbCommand commandResult.NewEvents
  let insertOrUpdate x = mapToDbOperation db dbCmd x
  insertOrUpdate derived
  ()

// let updateStreamAndEvents3 (db: DbContext) (appendEventResult: AppendEventsResult<'id, 'state, 'event, 'eventHeader>) =
//   let state, stream, events, _ = appendEventResult
//   updateStreamAndEvents db stream events

let updateEventStream (db: DbContext) (appendEventResult: OperationResult<'id, 'state, 'event, 'eventHeader>) =
  updateStreamAndEvents db appendEventResult.NewStream appendEventResult.NewEvents

let efCreate<'id, 'state, 'event, 'header, 'command, 'sideEffect, 'Db when 'Db :> DbContext and 'id: equality>
  (zero: 'state)
  (evolve: Evolve<'id, 'state, 'event, 'header>)
  (decide: PureDecide<'id, 'state, 'command, 'event, 'header, 'sideEffect>)
  : EfStore<'id, 'state, 'command, 'event, 'header, 'sideEffect, 'Db> =
  let rehydrateRaw = TinyEventStore.PureStore.rehydrate zero evolve

  let commandHandler =
    efCommandHandler<'id, 'state, 'command, 'event, 'header, 'sideEffect, 'Db> zero evolve decide

  let appendEventsHandler =
    efAppendEvents<'id, 'state, 'event, 'header, 'Db> zero evolve

  let rehydrateLatest = efRehydrate<'id, 'state, 'event, 'header, 'Db> zero evolve

  { decide = commandHandler
    append = appendEventsHandler
    rehydrateLatest = rehydrateLatest
    rehydrate = TinyEventStore.PureStore.rehydrate zero evolve
    getDb = fun ctx -> ctx.GetService<'Db>()
    updateEventStore =
      fun ctx operationResult ->
        let db = ctx.GetService<'Db>()
        updateEventStream db operationResult }
