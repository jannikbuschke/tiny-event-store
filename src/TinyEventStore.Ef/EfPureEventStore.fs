module TinyEventStore.EfPure

open System
open System.Collections.Generic
open System.Runtime.CompilerServices
open Microsoft.EntityFrameworkCore
open System.Text.Json
open System.Text.Json.Serialization
open Microsoft.EntityFrameworkCore.Metadata.Builders
open Microsoft.Extensions.DependencyInjection
open TinyEventStore
open FsToolkit.ErrorHandling
open TinyEventStore.EfUtils
open Json

[<AbstractClass>]
type AbstractStorableStream<'id>() =
  abstract member Id: 'id with get, set
  member val Version = Unchecked.defaultof<uint32> with get, set
  member val Created = Unchecked.defaultof<DateTimeOffset> with get, set

and StorableStream<'id, 'event, 'header>() =
  inherit AbstractStorableStream<'id>()
  let mutable id = Unchecked.defaultof<'id>
  override this.Id = id

  override this.Id
    with set value = id <- value

  member val Children = Unchecked.defaultof<ICollection<StorableEvent<'id, 'event, 'header>>> with get, set
and CausationType = | Command = 1 | Event = 2
and Causation = {
  Type: CausationType
  Id: Guid
}
and [<AbstractClass>] AbstractStorableEvent<'id>() =
  // abstract member StreamId: 'id with get, set
  member val EventId = Unchecked.defaultof<EventId> with get, set
  member val Version = Unchecked.defaultof<uint32> with get, set
  member val Timestamp = Unchecked.defaultof<DateTimeOffset> with get, set
  member val Causation = Unchecked.defaultof<Causation option> with get, set
  member val CorrelationId = Unchecked.defaultof<CorrelationId option> with get, set
// member val Payload: 'event
// member val Header = Unchecked.defaultof<'header> with get,set
and StorableEvent<'id, 'event, 'header>() =
  inherit AbstractStorableEvent<'id>()
  // let mutable streamId = Unchecked.defaultof<'id>
  // override this.StreamId = streamId
  //
  // override this.StreamId
  //   with set value = streamId <- value

  member val StreamId = Unchecked.defaultof<'id> with get, set
  member val Stream = Unchecked.defaultof<StorableStream<'id, 'event, 'header>> with get, set
  member val Data = Unchecked.defaultof<'event> with get, set

module Storable =

  let toStorableEvent(result: EventEnvelope<'id,'event,'header>) =
    //TODO: here maybe add a converter?
    StorableEvent<'id, 'event, 'header>(
      StreamId = result.StreamId,
      EventId = result.EventId,
      Version = result.Version,
      Timestamp = result.Timestamp,
      // Causation = result.CausationId |> Option.map(fun causationId -> match causationId with
      //                                                                 | CausationId.CommandId id-> { Type = CausationType.Command;Id=id }
      //                                                                 | CausationId.EventId id-> { Type = CausationType.Event;Id=id }),
      CorrelationId = result.CorrelationId,
      Data = result.Payload)

  let toStorableStream(result: Stream<'id,'event,'header>) =
    StorableStream<'id, 'event, 'header>(
      Id = result.Id,
      Version = result.Version,
      Created = result.Created//,
      // Children = result.Events |> List.map toStorableEvent |> List.toSeq
    )

  let toEvent(storableEvent: StorableEvent<'id, 'event, 'header>) =
    let result: EventEnvelope<'id,'event,'header> =
      { StreamId = storableEvent.StreamId
        Payload = storableEvent.Data
        EventId = storableEvent.EventId
        CausationId = None
                  // storableEvent.Causation |> Option.map(fun causation -> match causation.Type with
                  // | CausationType.Command -> causation.Id |> CommandId.FromRawValue |> CausationId.CommandId
                  // | CausationType.Event -> causation.Id |> EventId.FromRawValue |> CausationId.EventId
                  // )
        CorrelationId = storableEvent.CorrelationId
        Version = storableEvent.Version
        Timestamp = storableEvent.Timestamp
        Header = Unchecked.defaultof<'header>
      // this.Header
      }
    result

  let toStream(this: StorableStream<'id, 'event, 'header>) =
    let result: Stream<'id, 'event, 'header> =
      { Id = this.Id
        Version = this.Version
        Created = this.Created
        Events = this.Children |> Seq.map toEvent |> ResizeArray}

    result

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
  entity
let configureEventEnvelope<'id, 'rawId, 'event, 'header> (modelBuilder: ModelBuilder) (tableName: string) (idConverter: IdConverter<'id, 'rawId>) =
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

let configureStream<'id, 'rawId, 'event, 'header> (modelBuilder: ModelBuilder) (converter: IdConverter<'id, 'rawId>) (tableName: string) =
  let entity =  modelBuilder.Entity<Stream<'id, 'event, 'header>>()
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
  let streamBuilder = configureStream<'id, 'rawId, 'event, 'header> modelBuilder converter streamTableName
  let eventBuilder =  configureEventEnvelopeWithConversion<'id, 'rawId, 'event, 'event, 'header, 'header> modelBuilder eventTableName converter (id, id) (id, id)
  streamBuilder, eventBuilder
let configureEventStoreWithConversions<'id, 'rawId, 'event, 'eventDto, 'header, 'headerDto>
  (modelBuilder: ModelBuilder)
  (converter: IdConverter<'id, 'rawId>)
  (eventConverter: IdConverter<'event, 'eventDto>)
  (headerConverter: IdConverter<'header, 'headerDto>)
  (streamTableName: string)
  (eventTableName: string)
  =
  configureStream<'id, 'rawId, 'event, 'header> modelBuilder converter streamTableName

  configureEventEnvelopeWithConversion<'id, 'rawId, 'event, 'eventDto, 'header, 'headerDto> modelBuilder eventTableName converter eventConverter headerConverter

[<AbstractClass>]
type ContainerBase() =
  abstract member Id: int with get, set
// member val Children = Unchecked.defaultof<ICollection<ChildBase>> with get,set

and ConcreteContainer<'t>() =
  inherit ContainerBase()
  // override _.Id: int = 0 with get,set
  // override this.Id with set value = failwith "todo"
  let mutable id = 0
  override this.Id = id

  override this.Id
    with set value = id <- value

  member val Children = Unchecked.defaultof<ICollection<Child<'t>>> with get, set
// member val Value:'t = Unchecked.defaultof<'t> with get,set
// member val Children:'t ICollection = Unchecked.defaultof<'t> with get,set
// member this.Children=children

and [<AbstractClass>] ChildBase() =
  abstract member Id: int with get, set
// member val ParentId = Unchecked.defaultof<int> with get,set
// member val Parent = Unchecked.defaultof<ContainerBase> with get,set
and Child<'t>() =
  inherit ChildBase()
  let mutable id = 0
  override this.Id = id

  override this.Id
    with set value = id <- value

  member val ParentId = Unchecked.defaultof<int> with get, set
  member val Parent = Unchecked.defaultof<ConcreteContainer<'t>> with get, set
  member val Data = Unchecked.defaultof<string> with get, set

[<Extension>]
type ModelBuilderExtensions() =

  [<Extension>]
  static member AddMultiEventStore<'id, 'idRaw>(ty: ModelBuilder, idConverter: IdConverter<'id, 'idRaw>, fStream, fEvent, configureChilds) =

    // STREAM
    // configure shared props
    ty.Entity<AbstractStorableStream<'id>>(fun entity ->
      entity.HasKey(fun x -> x.Id :> obj) |> ignore

      entity.ToTable "streams" |> ignore

      entity
        .Property(fun x -> x.Id)
        .HasConversion(idConverter |> fst, idConverter |> snd)
      |> ignore)
    |> ignore

    // configure discriminator
    let streamEntity =
      ty.Entity<AbstractStorableStream<'id>>().HasDiscriminator<string>("Streamtype")
    // .HasValue<ContainerBase>("container_base")
    // .HasValue<ConcreteContainer<string>>("string_container")
    // .HasValue<ConcreteContainer<int>>("string_int")

    fStream streamEntity

    // EVENT
    // configure shared props
    ty.Entity<AbstractStorableEvent<'id>>(fun entity ->
      entity.HasKey(fun x -> x.EventId :> obj) |> ignore

      entity
        .Property(fun x -> x.EventId)
        .HasConversion(EventId.ToRawValue, EventId.FromRawValue)
      |> ignore

      entity.ToTable "events" |> ignore
      // entity
      //   .HasIndex(fun x -> (x.Version, x.StreamId) :> obj)
      //   .IsUnique()
      //   .IsDescending(false, false)
      // |> ignore
      //
      //   // maybe split this into two columns, one column "CausationType": string option, and a second column "CausationValue": guid option
      //   // probably wrong
      entity
        .OwnsOne(fun x->x.Causation)
      |> ignore
      //
      let toRaw = Option.map CorrelationId.ToRawValue >> Option.toNullable

      let fromRaw = Option.ofNullable >> Option.map CorrelationId.FromRawValue

      entity.Property(fun x -> x.CorrelationId).HasConversion(toRaw, fromRaw)
      |> ignore
      //
      // entity
      //   .Property(fun x -> x.StreamId)
      //   .HasConversion(idConverter |> fst, idConverter |> snd)
      // |> ignore
      //
      //   let serializeEvent (e: 'event) =
      //     e |> (payloadConverter |> fst) |> serialize
      //
      //   let deserializeEvent (dto: string) =
      //     dto |> deserialize<'eventDto> |> (payloadConverter |> snd)
      //
      //   entity
      //     .Property(fun x -> x.Payload)
      //     .HasConversion(serializeEvent, deserializeEvent)
      //   |> ignore
      //
      //   let serializeHeader (e: 'header) =
      //     e |> (headerConverter |> fst) |> serialize
      //
      //   let deserializeHeader (dto: string) =
      //     dto |> deserialize<'headerDto> |> (headerConverter |> snd)
      //
      //   entity
      //     .Property(fun x -> x.Header)
      //     .HasConversion(serializeHeader, deserializeHeader)
      //   |> ignore

      ())
    |> ignore

    // configure discriminator
    let eventEntity =
      ty.Entity<AbstractStorableEvent<'id>>().HasDiscriminator<string>("Eventtype")

    fEvent eventEntity

    ()

  [<Extension>]
  static member MultiTest(ty: ModelBuilder) =
    // let container1 = ConcreteContainer<string>()
    ty
      .Entity<ContainerBase>()
      .HasDiscriminator<string>("Streamtype")
      .HasValue<ContainerBase>("container_base")
      .HasValue<ConcreteContainer<string>>("string_container")
      .HasValue<ConcreteContainer<int>>("string_int")

    ty
      .Entity<ChildBase>()
      .HasDiscriminator<string>("Eventtype")
      .HasValue<ChildBase>("child_base")
      .HasValue<Child<string>>("string_child")
      .HasValue<Child<int>>("int_child")

    // TODO
    ty
      .Entity<Child<string>>()
      .HasOne(fun x -> x.Parent)
      .WithMany(fun x -> x.Children :> IEnumerable<Child<string>>)
      .HasForeignKey(fun x -> x.ParentId :> obj)
      .HasConstraintName("k1")
    |> ignore

    ty
      .Entity<Child<int>>()
      .HasOne(fun x -> x.Parent)
      .WithMany(fun x -> x.Children :> IEnumerable<Child<int>>)
      .HasForeignKey(fun x -> x.ParentId :> obj)
      .HasConstraintName("k1")
    |> ignore
    // ty.Entity<ChildBase>().HasOne(fun x->x.Parent).WithMany(fun x -> x.Children:>IEnumerable<ChildBase>).HasForeignKey(fun x->x.ParentId:>obj) |> ignore

    ty
      .Entity<Child<string>>()
      .Property(fun x -> x.ParentId)
      .HasColumnName("ParentId")

    ty.Entity<Child<int>>().Property(fun x -> x.ParentId).HasColumnName("ParentId")

    ty.Entity<Child<string>>().Property(fun x -> x.Data).HasColumnName("Data")
    ty.Entity<Child<int>>().Property(fun x -> x.Data).HasColumnName("Data")

    let x2: ConcreteContainer<string> = ConcreteContainer<string>(Id = 2)
    let x3: ConcreteContainer<int> = ConcreteContainer<int>(Id = 3)

    ty.Entity<ConcreteContainer<string>>().HasData(x2)
    ty.Entity<ConcreteContainer<int>>().HasData(x3)
    let c2 = Child<int>(Id = 2, ParentId = 2)
    let c3 = Child<string>(Id = 3, ParentId = 3)
    let c4 = Child<string>(Id = 4, ParentId = 3)
    ty.Entity<Child<int>>().HasData(c2)
    ty.Entity<Child<string>>().HasData(c3)
    ty.Entity<Child<string>>().HasData(c4)
    ()

  [<Extension>]
  static member AddEventStore<'id, 'rawId, 'event, 'header>(ty: ModelBuilder, converter: IdConverter<'id, 'rawId>, entityName: string) =
    configureEventStore<'id, 'rawId, 'event, 'header> ty converter (sprintf "%s.Streams" entityName) (sprintf "%s.Events" entityName)

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

// USED
let genericLoadEvents<'id, 'event, 'header when 'id: equality> (db: DbContext) (id: 'id) =
  task {
    // let! stream =
    //   db
    //     .Set<StorableStream<'id, 'event, 'header>>()
    //     .Include(fun x -> x.Children)
    //     .AsNoTracking()
    //     .SingleOrDefaultAsync(fun x -> x.Id = id)

    let! stream =
       db
         .Set<Stream<'id, 'event, 'header>>()
         .Include(fun x -> x.Events)
         .AsNoTracking()
         .SingleOrDefaultAsync(fun x -> x.Id = id)

    let stream: Stream<'id,'event,'header> =
      if box stream = null then
        let stream =
          { Stream.Id = id
            Version = 0u
            Created = DateTimeOffset.UtcNow
            Events = ResizeArray([])  }
        stream
      else
        stream
        // let coreStream = Storable.toStream stream
        // coreStream

    return Result.Ok(stream)
  }

let genericCommit<'id, 'event, 'header, 'sideEffect> (db: DbContext) (events: EventEnvelope<'id, 'event, 'header> list, sideEffects: 'sideEffect list) =
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

let efCommandHandler<'id, 'state, 'command,'ch, 'event, 'header, 'sideEffect, 'Db when 'Db :> DbContext and 'id: equality>
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

// maybe
type EfStore<'id, 'state, 'command, 'commandHeader, 'event, 'header, 'sideEffect, 'Db when 'Db :> DbContext> =
  { decide:
      IServiceProvider -> 'id -> TaskResult<CommandEnvelope<'id, 'command, 'commandHeader> -> Result<CommandResult<'id, 'state, 'event, 'header, 'sideEffect>, string>, string>
    append: IServiceProvider -> 'id -> ('event * 'header) list -> TaskResult<AppendEventsResult<'id, 'state, 'event, 'header>, string>
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
  updateStorableStreamAndEvents db appendEventResult.NewStream appendEventResult.NewEvents
  // updateStreamAndEvents db appendEventResult.NewStream appendEventResult.NewEvents

let efCreate<'id, 'state, 'event, 'header, 'command,'commandHeader, 'sideEffect, 'Db when 'Db :> DbContext and 'id: equality>
  (zero: 'state)
  (evolve: Evolve<'id, 'state, 'event, 'header>)
  (decide: PureDecide<'id, 'state, 'command, 'commandHeader, 'event, 'header, 'sideEffect>)
  : EfStore<'id, 'state, 'command,'commandHeader, 'event, 'header, 'sideEffect, 'Db> =
  let rehydrateRaw = TinyEventStore.PureStore.rehydrate zero evolve

  let commandHandler =
    efCommandHandler<'id, 'state, 'command,'commandHeader, 'event, 'header, 'sideEffect, 'Db> zero evolve decide

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
