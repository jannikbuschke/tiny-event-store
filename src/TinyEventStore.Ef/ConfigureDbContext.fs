module TinyEventStore.Ef.DbContext

open System.Runtime.CompilerServices
open Microsoft.EntityFrameworkCore
open TinyEventStore
open FsToolkit.ErrorHandling
open TinyEventStore.EfUtils
open Json
open TinyEventStore.Ef.Storables

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
    .Property(fun x -> x.Version)
    .HasConversion((fun x -> int x), (fun x -> uint x))
  |> ignore

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

  let toRaw =
    Option.map CorrelationId.ToRawValue
    >> Option.toNullable

  let fromRaw =
    Option.ofNullable
    >> Option.map CorrelationId.FromRawValue

  entity
    .Property(fun x -> x.CorrelationId)
    .HasConversion(toRaw, fromRaw)
  |> ignore

  entity
    .Property(fun x -> x.StreamId)
    .HasConversion(idConverter |> fst, idConverter |> snd)
  |> ignore

  let serializeEvent (e: 'event) =
    e |> (payloadConverter |> fst) |> serialize

  let deserializeEvent (dto: string) =
    dto
    |> deserialize<'eventDto>
    |> (payloadConverter |> snd)

  entity
    .Property(fun x -> x.Payload)
    .HasConversion(serializeEvent, deserializeEvent)
  |> ignore

  let serializeHeader (e: 'header) =
    e |> (headerConverter |> fst) |> serialize

  let deserializeHeader (dto: string) =
    dto
    |> deserialize<'headerDto>
    |> (headerConverter |> snd)

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
  modelBuilder.Entity<EventEnvelope<'id, 'event, 'header>> (fun entity ->
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

    let toRaw =
      Option.map CorrelationId.ToRawValue
      >> Option.toNullable

    let fromRaw =
      Option.ofNullable
      >> Option.map CorrelationId.FromRawValue

    entity
      .Property(fun x -> x.CorrelationId)
      .HasConversion(toRaw, fromRaw)
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
  let entity = modelBuilder.Entity<Stream<'id, 'event, 'header>>()
  entity.HasKey(fun x -> x.Id :> obj) |> ignore

  entity.ToTable tableName |> ignore

  entity
    .Property(fun x -> x.Id)
    .HasConversion(converter |> fst, converter |> snd)
  |> ignore

  entity

let configureEventStore<'id, 'rawId, 'event, 'header>
  (modelBuilder: ModelBuilder)
  (converter: IdConverter<'id, 'rawId>)
  (streamTableName: string)
  (eventTableName: string)
  =
  let streamBuilder =
    configureStream<'id, 'rawId, 'event, 'header> modelBuilder converter streamTableName

  let eventBuilder =
    configureEventEnvelopeWithConversion<'id, 'rawId, 'event, 'event, 'header, 'header> modelBuilder eventTableName converter (id, id) (id, id)

  streamBuilder, eventBuilder

let configureEventStoreWithConversions<'id, 'rawId, 'event, 'eventDto, 'header, 'headerDto>
  (modelBuilder: ModelBuilder)
  (converter: IdConverter<'id, 'rawId>)
  (eventConverter: IdConverter<'event, 'eventDto>)
  (headerConverter: IdConverter<'header, 'headerDto>)
  (streamTableName: string)
  (eventTableName: string)
  =
  configureStream<'id, 'rawId, 'event, 'header> modelBuilder converter streamTableName |> ignore

  configureEventEnvelopeWithConversion<'id, 'rawId, 'event, 'eventDto, 'header, 'headerDto> modelBuilder eventTableName converter eventConverter headerConverter

type ConfigureStream<'id, 'idRaw when 'id: equality>
  (
    ty: ModelBuilder,
    streamEntityDiscriminator: Metadata.Builders.DiscriminatorBuilder<string>,
    eventEntityDiscriminator: Metadata.Builders.DiscriminatorBuilder<string>
  ) =
  member this.WithStreamType<'event, 'header>(name: string) =
    streamEntityDiscriminator.HasValue<StorableStream<'id, 'event, 'header>> name
    |> ignore

    eventEntityDiscriminator.HasValue<StorableEvent<'id, 'event, 'header>> name
    |> ignore

    // TODO: StreamId has conversion
    let eventEntity = ty.Entity<StorableEvent<'id, 'event, 'header>>()

    eventEntity
      .Property(fun x -> x.StreamId)
      .HasColumnName("StreamId")
    |> ignore

    eventEntity
      .HasIndex(fun x -> (x.Version, x.StreamId) :> obj)
      .IsUnique()
      // .IsDescending(true, false)
      // .HasDatabaseName("asd")
    |> ignore

    let dataProp =
      ty
        .Entity<StorableEvent<'id, 'event, 'header>>()
        .Property(fun x -> x.Data)

    dataProp.HasColumnName("Data") |> ignore

    dataProp.HasConversion(Json.serialize, Json.deserialize)
    |> ignore

    let headerProp =
      ty
        .Entity<StorableEvent<'id, 'event, 'header>>()
        .Property(fun x -> x.Header)

    headerProp.HasConversion(Json.serialize, Json.deserialize)
    |> ignore

    headerProp.HasColumnName("Header") |> ignore

    ty
      .Entity<StorableEvent<'id, 'event, 'header>>()
      .HasOne(fun x -> x.Stream)
      .WithMany(fun x -> x.Children :> System.Collections.Generic.IEnumerable<StorableEvent<'id, 'event, 'header>>)
      .HasForeignKey(fun x -> x.StreamId :> obj)
      .HasConstraintName("stream_events")
    |> ignore

    ()

  member this.Build() = ()

[<Extension>]
type ModelBuilderExtensions() =

  [<Extension>]
  static member HasTinyEventStoreJsonConversion<'a>(property: Metadata.Builders.PropertyBuilder<'a>) =
    property.HasConversion(Json.serialize, Json.deserialize) |> ignore

  [<Extension>]
  static member HasTinyEventStoreIdConversion<'id,'idRaw>(property: Metadata.Builders.PropertyBuilder<'id>, idConverter: IdConverter<'id, 'idRaw>) =
    property.HasConversion(idConverter |> fst, idConverter |> snd) |> ignore

  [<Extension>]
  static member AddMultiEventStore2<'id, 'idRaw when 'id: equality>(ty: ModelBuilder, idConverter: IdConverter<'id, 'idRaw>, name, fn) =
    ty.Entity<AbstractStorableStream<'id>> (fun entity ->
      entity.HasKey(fun x -> x.Id :> obj) |> ignore

      entity.ToTable (name + "_streams") |> ignore

      entity
        .Property(fun x -> x.Id)
        .HasConversion(idConverter |> fst, idConverter |> snd)
      |> ignore)
    |> ignore

    ty.Entity<AbstractStorableEvent<'id>> (fun entity ->
      entity.HasKey(fun x -> x.SequenceId :> obj)
      |> ignore

      entity
        .Property(fun x -> x.EventId)
        .HasConversion(EventId.ToRawValue, EventId.FromRawValue)
      |> ignore

      entity.ToTable(name + "_events") |> ignore
      entity.OwnsOne(fun x -> x.CausationId) |> ignore

      let toRaw =
        Option.map CorrelationId.ToRawValue
        >> Option.toNullable

      let fromRaw =
        Option.ofNullable
        >> Option.map CorrelationId.FromRawValue

      entity
        .Property(fun x -> x.CorrelationId)
        .HasConversion(toRaw, fromRaw)
      |> ignore

      entity.Ignore(fun x->x.CausationId:>obj)|>ignore
      entity.Ignore(fun x->x.CorrelationId:>obj)|>ignore
      ())
    |> ignore

    let streamEntity =
      ty
        .Entity<AbstractStorableStream<'id>>()
        .HasDiscriminator<string>("Type")

    let eventEntity =
      ty
        .Entity<AbstractStorableEvent<'id>>()
        .HasDiscriminator<string>("Type")

    fn (ConfigureStream<'id, 'idRaw>(ty, streamEntity, eventEntity))
    (ty.Entity<AbstractStorableStream<'id>> ()), (ty.Entity<AbstractStorableEvent<'id>>())

  [<Extension>]
  static member AddMultiEventStore<'id, 'idRaw when 'id: equality>(ty: ModelBuilder, idConverter: IdConverter<'id, 'idRaw>, fStream, fEvent, configureChilds) =
    ty.Entity<AbstractStorableStream<'id>> (fun entity ->
      entity.HasKey(fun x -> x.Id :> obj) |> ignore

      entity.ToTable "streams" |> ignore

      entity
        .Property(fun x -> x.Id)
        .HasConversion(idConverter |> fst, idConverter |> snd)
      |> ignore)
    |> ignore

    let streamEntity =
      ty
        .Entity<AbstractStorableStream<'id>>()
        .HasDiscriminator<string>("Streamtype")

    fStream streamEntity

    ty.Entity<AbstractStorableEvent<'id>> (fun entity ->
      entity.HasKey(fun x -> x.EventId :> obj) |> ignore

      entity
        .Property(fun x -> x.EventId)
        .HasConversion(EventId.ToRawValue, EventId.FromRawValue)
      |> ignore

      entity.ToTable "events" |> ignore

      entity.OwnsOne(fun x -> x.CausationId) |> ignore

      let toRaw =
        Option.map CorrelationId.ToRawValue
        >> Option.toNullable

      let fromRaw =
        Option.ofNullable
        >> Option.map CorrelationId.FromRawValue

      entity
        .Property(fun x -> x.CorrelationId)
        .HasConversion(toRaw, fromRaw)
      |> ignore

      ())
    |> ignore

    let eventEntity =
      ty
        .Entity<AbstractStorableEvent<'id>>()
        .HasDiscriminator<string>("Eventtype")

    fEvent eventEntity

    ()

  [<Extension>]
  static member AddEventStore<'id, 'rawId, 'event, 'header>(ty: ModelBuilder, converter: IdConverter<'id, 'rawId>, entityName: string) =
    configureEventStore<'id, 'rawId, 'event, 'header> ty converter (sprintf "%s.Streams" entityName) (sprintf "%s.Events" entityName)

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
