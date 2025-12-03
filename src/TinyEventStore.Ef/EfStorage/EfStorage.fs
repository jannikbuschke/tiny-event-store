namespace TinyEventStore.EfStorage

open TinyEventStore.Interfaces
open FsToolkit.ErrorHandling
open System
open Microsoft.EntityFrameworkCore
open System.Linq
open System.Runtime.CompilerServices
open Core
open Microsoft.EntityFrameworkCore.Storage.ValueConversion

type Converter<'t, 'traw> = ('t -> 'traw) * ('traw -> 't)

type EventEnvelope<'id, 'details> =
  {
    StreamId: 'id
    EventId: TinyEventStore.EventId
    Details: 'details
    Version: V
    TimeStamp: DateTimeOffset
    Causation: TinyEventStore.Causation option
  }

  interface IEvent with
    member this.TimeStamp = this.TimeStamp
    member this.Version = this.Version

type Stream<'id> =
  {
    Id: 'id
    Version: V
    Created: DateTimeOffset
    Modified: DateTimeOffset
  }

  interface IStream with
    member this.Version = this.Version
    member this.Modified = this.Modified
    member this.Created = this.Created

type EventStorageOptions<'streamId, 'streamIdRaw> =
  {
    TableNamePrefix: string
    StreamId: Converter<'streamId, 'streamIdRaw>
  // Stream: Converter<'stream * 'streamId * V, Dtos.StreamDto<'streamIdRaw, 'eventDetails>>
  // Event: Converter<EventEnvelope<'streamId, 'eventDetails>, Dtos.EventDto<'streamIdRaw, 'eventDetails>>
  // CreateStream: StreamCreator<'streamId, 'stream, 'state, 'eventDetails>
  // UpdateStream: StreamUpdater<'streamId, 'stream, 'state, 'eventDetails>
  }

open TinyEventStore.Log
open TinyEventStore.Log.Types

module private Helpers =

  let correlationIdConverter () =
    let toRaw = Option.map TinyEventStore.CorrelationId.ToRawValue >> Option.toNullable
    let fromRaw =
      Option.ofNullable >> Option.map TinyEventStore.CorrelationId.FromRawValue
    ValueConverter<TinyEventStore.CorrelationId option, Nullable<Guid>>(toRaw, fromRaw)

module Seq =
  let toSeqAsync (q: IQueryable<'t>) = q.ToListAsync()

module Result =
  let iterError f x =
    match x with
    | Ok _ -> ()
    | Error e -> f e

type EfStorage<'streamId, 'streamIdRaw, 'state, 'eventDetails, 'c, 'db
  when 'db :> DbContext and 'streamId: equality and 'streamIdRaw: equality>
  (db: 'db, options: EventStorageOptions<'streamId, 'streamIdRaw>) =

  let logger = LogProvider.getLoggerByName "TinyEventStore.EfStorage"
  let toRawStreamId = options.StreamId |> fst
  let toStreamId = options.StreamId |> snd
  let toEventEnvelope (dto: Dtos.EventDto<_, _>) =
    if box dto = null then
      failwith "dto is null"
    else
      {
        StreamId = dto.StreamId |> toStreamId
        EventId = dto.EventId |> TinyEventStore.EventId.FromRawValue
        Details = dto.Data
        Version = dto.Version
        TimeStamp = dto.Timestamp
        Causation = None
      // CausationId=None
      // dto.CausationId
      // |> Option.map(fun x ->
      //   match x.Type with
      //   |Dtos.CausationType.Event ->  x.Id|>TinyEventStore.EventId.FromRawValue|>TinyEventStore.CausationId.EventId
      //   |Dtos.CausationType.Command ->  x.Id|>TinyEventStore.CommandId.FromRawValue |> TinyEventStore.CausationId.CommandId
      //   |_ -> failwith "unknown causationtype"
      //   )
      }

  let toEventDto (e: EventEnvelope<_, _>) =
    let streamId = e.StreamId |> toRawStreamId
    let eventId = e.EventId |> TinyEventStore.EventId.ToRawValue
    Dtos.EventDto(
      StreamId = streamId,
      EventId = eventId,
      Version = e.Version,
      Timestamp = e.TimeStamp,
      CausationId = None,

      // CausationId = (e.CausationId
      //    |> Option.map(
      //    function
      //    | TinyEventStore.CausationId.CommandId v -> Dtos.StorableCausationId(Id = (v |> TinyEventStore.CommandId.ToRawValue), Type = Dtos.CausationType.Command)
      //    | TinyEventStore.CausationId.EventId v -> Dtos.StorableCausationId(Id = (v |> TinyEventStore.EventId.ToRawValue), Type = Dtos.CausationType.Event)
      //   )),

      Data = e.Details
    )

  let toStream (dto: Dtos.StreamDto<_, _>) =
    {
      Id = dto.Id |> toStreamId
      Version = dto.Version
      Created = dto.Created
      Modified = dto.Modified
    }

  let toStreamDto (stream: Stream<_>) =
    Dtos.StreamDto(
      Id = (stream.Id |> toRawStreamId),
      Version = stream.Version,
      Created = stream.Created,
      Modified = stream.Modified
    )

  let idConverter = options.StreamId

  let converToList events =
    events |> Seq.map toEventEnvelope |> Seq.toList

  let createStream: StreamCreator<_, _, _, _> =
    fun appendEventsResult ->
      {
        Version = appendEventsResult.Version
        Created = appendEventsResult.TimeStamp
        Modified = appendEventsResult.TimeStamp
        Id = appendEventsResult.Id
      }

  let updateStream: StreamUpdater<_, _, _, _> =
    fun stream0 appendEventsResult ->
      { stream0 with
          Version = appendEventsResult.Version
          Modified = appendEventsResult.TimeStamp
      }

  member _.Db = db

  member _.StreamSet() =
    db.Set<Dtos.StreamDto<'streamIdRaw, 'eventDetails>>()

  member this.QueryStreams() = this.StreamSet().AsNoTracking()

  member this.GetStream id =
    task {
      let streamId = id |> (idConverter |> fst)
      let! stream = this.StreamSet().Include(_.Children).AsNoTracking().FirstOrDefaultAsync(fun x -> x.Id = streamId)
      return stream
    }

  member _.EventSet() =
    db.Set<Dtos.EventDto<'streamIdRaw, 'eventDetails>>()

  member this.QueryEvents() =
    this.EventSet().OrderBy(fun x -> x.Version).AsNoTracking()

  member this.QueryStreamEvents(id: 'streamId) =
    let streamId = id |> (idConverter |> fst)
    this.QueryEvents().Where(fun x -> x.StreamId = streamId).AsNoTracking()

  interface IEventStorage<'streamId, Stream<'streamId>, 'state, EventEnvelope<'streamId, 'eventDetails>, 'c> with

    member this.LoadEventRange(id: 'streamId, from, until) =
      id
      |> this.QueryStreamEvents
      |> Seq.toSeqAsync
      |> Task.map (converToList >> Some)

    member this.LoadEventsFrom(id, from) =
      id
      |> this.QueryStreamEvents
      |> Seq.toSeqAsync
      |> Task.map (converToList >> Some)

    member this.LoadAllEvents id =
      id
      |> this.QueryStreamEvents
      |> Seq.toSeqAsync
      |> Task.map (converToList >> Some)

    member this.LoadStream(id: 'streamId) =
      task {
        logger.debug (Log.setMessage "Loading stream {stream_id}" >> Log.addParameter id)
        let! stream = this.GetStream id
        if box stream = null then
          logger.debug (Log.setMessage "Stream {stream_id} not found" >> Log.addParameter id)
          return None
        // return Error(EventStoreError.New(EventStoreErrorDetails.NotFound, None))
        else
          let! events = this.QueryStreamEvents(id).ToListAsync()

          let events = events |> Seq.map toEventEnvelope |> Seq.toList
          logger.debug (
            Log.setMessage "Loaded events for {stream_id}"
            >> Log.addParameter id
            >> Log.addContext "Events" events
          )
          return
            {
              Id = id
              Stream = stream |> toStream
              Events = events
            }
            |> Some

      }

    member this.LoadRequiredStream(id: 'streamId) =
      task {
        let! stream = (this :> IEventStorage<_, _, _, _, _>).LoadStream id
        let stream =
          stream
          |> Result.requireSome (EventStoreError.New(EventStoreErrorDetails.NotFound, None))
        stream
        |> Result.iterError (fun e ->
          logger.error (
            Log.setMessage "Loading required stream {stream} errored {error}"
            >> Log.addParameter id
            >> Log.addParameter e
          )
        )
        return stream
      }

    member this.Commit v =
      taskResult {
        logger.debug (
          Log.setMessage "Committing events. Stream is new = {isNew}"
          >> Log.addParameter v.IsNew
          >> Log.addContext "Events" v.Events
        )
        let! stream = (this :> IEventStorage<_, _, _, _, _>).LoadStream v.Id

        let! s1 =
          if v.IsNew then
            createStream v |> Ok
          else
            if stream.IsNone then
              logger.error (Log.setMessage "v.IsNew but no stream found")
            stream
            |> Option.map (fun x -> updateStream x.Stream v |> Ok)
            |> Option.defaultValue (EventStoreError.New(EventStoreErrorDetails.NotFound, None) |> Error)
        // printfn "Stream to be saved\n%A" s1
        let streamDto = s1 |> toStreamDto
        if v.IsNew then
          this.StreamSet().Add streamDto |> ignore
        else
          let existingEntry =
            this.StreamSet().Local.FirstOrDefault(fun x -> x.Id = streamDto.Id)
          if box existingEntry = null then
            // printfn "attach..."
            this.StreamSet().Attach streamDto |> ignore
            let entry = this.StreamSet().Entry streamDto
            // printfn "entry %A" entry
            // printfn "is modified %A" (entry.Property(_.Version).IsModified)
            // assert
            entry.Property(_.Version).IsModified <- true
            entry.Property(_.Modified).IsModified <- true
          else
            // printfn "set current values"
            let entry = this.StreamSet().Entry existingEntry
            entry.CurrentValues.SetValues streamDto
        // entry.State<-EntityState.Modified

        let eventDtos = v.Events |> Seq.map toEventDto
        // printfn "debug view\n%s" (this.Db :> DbContext).ChangeTracker.DebugView.LongView

        eventDtos |> Seq.iter (this.EventSet().Add >> ignore)

        logger.debug (
          Log.setMessage "Saving"
          >> Log.addContext "EfDebugView" db.ChangeTracker.DebugView.LongView
        )

        let! result = db.SaveChangesAsync()

        logger.info (Log.setMessage "SaveChanges {count}" >> Log.addParameter result)

        return ()
      }

    member this.QueryStreams() =
      this.StreamSet()
      |> Seq.toSeqAsync
      |> Task.map (Seq.map (fun x -> x.Id |> toStreamId, x |> toStream) >> Seq.toList >> Ok)

  member this.LoadAllEvents id =
    (this :> IEventStorage<_, _, _, _, _>).LoadAllEvents id

open Helpers

type ConfigureStreamHelper<'streamIdRaw, 'eventIdRaw, 'discriminator when 'streamIdRaw: equality>
  (
    ty: ModelBuilder,
    streamEntityDiscriminator: Metadata.Builders.DiscriminatorBuilder<'discriminator>,
    eventEntityDiscriminator: Metadata.Builders.DiscriminatorBuilder<'discriminator>
  ) =
  member _.WithStreamType<'streamId, 'streamIdRaw, 'stream, 'eventDetails when 'streamId: equality>
    (discriminatorValue: 'discriminator, cfg)
    =
    streamEntityDiscriminator.HasValue<Dtos.StreamDto<'streamIdRaw, 'eventDetails>> discriminatorValue
    |> ignore

    eventEntityDiscriminator.HasValue<Dtos.EventDto<'streamIdRaw, 'eventDetails>> discriminatorValue
    |> ignore

    let eventEntity = ty.Entity<Dtos.EventDto<'streamIdRaw, 'eventDetails>>()

    eventEntity.Property(fun x -> x.StreamId).IsRequired(true).HasColumnName "StreamId"
    // .HasConversion(toEfConverter options.StreamId)
    |> ignore

    eventEntity.HasIndex(fun x -> (x.Version, x.StreamId) :> obj).IsUnique()
    |> ignore

    let dataProp =
      ty
        .Entity<Dtos.EventDto<'streamIdRaw, 'eventDetails>>()
        .Property(fun x -> x.Data)
        .IsRequired(true)
        .HasColumnName("Data")
        .HasConversion(TinyEventStore.Json.serialize, TinyEventStore.Json.deserialize)


    // let headerProp =
    //   ty.Entity<StorableEvent<'id, 'event, 'header>>().Property(fun x -> x.Header)
    //
    // headerProp.HasConversion(Json.serialize, Json.deserialize) |> ignore
    //
    // headerProp.HasColumnName("Header") |> ignore

    ty
      .Entity<Dtos.EventDto<'streamIdRaw, 'eventDetails>>()
      .HasOne(fun x -> x.Stream)
      .WithMany(fun x -> x.Children :> Collections.Generic.IEnumerable<Dtos.EventDto<'streamIdRaw, 'eventDetails>>)
      .HasForeignKey(fun x -> x.StreamId :> obj)
      .HasConstraintName
      "stream_events"
    |> ignore

    cfg |> Option.iter (fun x -> x (eventEntity, dataProp))
    ()

type ModelBuilderExtensions() =
  [<Extension>]
  static member AddSharedEventStorage<'streamIdRaw, 'eventIdRaw, 'tDiscriminator
    when
    // static member AddSharedEventStorage<'streamId, 'streamIdRaw, 'id, 'idraw, 'stream, 'state, 'e, 'ed, 'c, 'tDiscriminator
    'streamIdRaw: equality
    and 'eventIdRaw: equality>
    (
      ty: ModelBuilder,
      tablePrefix: string,
      // options: EventStorageOptions<'streamId, 'streamIdRaw, 'id, 'idraw>,
      // system: System<'state, 'e, 'ed, 'c>,
      configure: ConfigureStreamHelper<'streamIdRaw, 'eventIdRaw, 'tDiscriminator> -> unit
    ) =
    // Abstract Stream
    let _ =
      ty.Entity<Dtos.StreamBaseDto<'streamIdRaw>>(fun entity ->
        entity.HasKey(fun x -> x.Id :> obj) |> ignore

        entity.ToTable(tablePrefix + "_streams") |> ignore

        entity.Property(fun x -> x.Id)
        // .HasConversion(toEfConverter (options.Converters.StreamId))
        |> ignore
      )

    // Abstract Event
    ty.Entity<Dtos.EventBaseDto<'eventIdRaw>>(fun entity ->
      entity.HasKey(fun x -> x.SequenceId :> obj) |> ignore

      entity.Property(fun x -> x.EventId).IsRequired true
      // .HasConversion(toEfConverter options.Converters.Id)
      |> ignore

      entity.ToTable(tablePrefix + "_events") |> ignore
      entity.OwnsOne(fun x -> x.CausationId) |> ignore

      entity.Property(fun x -> x.CorrelationId).HasConversion(correlationIdConverter ())
      |> ignore

      entity.Ignore(fun x -> x.CorrelationId :> obj) |> ignore

      ()
    )
    |> ignore

    let streamEntity =
      ty.Entity<Dtos.StreamBaseDto<'streamIdRaw>>().HasDiscriminator<'tDiscriminator>("Type")

    let eventEntity =
      ty.Entity<Dtos.EventBaseDto<'eventIdRaw>>().HasDiscriminator<'tDiscriminator>("Type")
    configure (ConfigureStreamHelper<'streamIdRaw, 'eventIdRaw, 'tDiscriminator>(ty, streamEntity, eventEntity))
    ()
