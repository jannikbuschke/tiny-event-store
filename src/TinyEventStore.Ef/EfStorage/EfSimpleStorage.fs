namespace TinyEventStore.EfSimpleStorage

open TinyEventStore.InterfacesSimple
open FsToolkit.ErrorHandling
open System
open Microsoft.EntityFrameworkCore
open System.Linq
open System.Runtime.CompilerServices
open Microsoft.EntityFrameworkCore.Storage.ValueConversion
open TinyEventStore.Log
open TinyEventStore.EfSimpleStorage.Dtos

type Converter<'t, 'traw> = ('t -> 'traw) * ('traw -> 't)

type EventStorageOptions<'e> =
  {
    TableNamePrefix: string
    ToEvent: EventDto<'e> -> 'e
    ToEventStorageObject: 'e -> EventDto<'e>
  }

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

type EfSimpleStorage<'state, 'e, 'c, 'db when 'db :> DbContext>(db: 'db, options: EventStorageOptions<'e>, getStreamKey: 'e -> Guid) =

  let logger = LogProvider.getLoggerByName "TinyEventStore.EfSimpleStorage"

  let converToList events =
    events |> Seq.map options.ToEvent |> Seq.toList

  member _.Db = db

  member _.StreamSet() = db.Set<StreamDto<'e>>()

  member this.QueryStreams() = this.StreamSet().AsNoTracking()

  member this.GetStream id =
    this
      .StreamSet()
      .Include(fun x -> x.Children.OrderBy(fun x -> x.Id))
      // .AsNoTracking()
      .FirstOrDefaultAsync(fun x -> x.Id = id)
    |> Task.map Option.ofObj

  member _.EventSet() = db.Set<EventDto<'e>>()

  member this.QueryEvents() =
    this.EventSet().OrderBy(_.Version).AsNoTracking()

  member this.QueryStreamEvents streamId =
    this.QueryEvents().Where(fun x -> x.StreamId = streamId).AsNoTracking()

  member this.LoadEventRange(id: Guid, from, until) =
    id
    |> this.QueryStreamEvents
    |> Seq.toSeqAsync
    |> Task.map (fun events ->
      let events = converToList events
      match events with
      | [] -> None
      | head :: tail ->
        {
          NonEmptyList.Head = head
          Tail = tail
        }
        |> Some
    )

  member this.LoadAllEvents(id: Guid) =
    id
    |> this.QueryStreamEvents
    |> Seq.toSeqAsync
    |> Task.map (fun events ->
      let events = converToList events
      match events with
      | [] -> None
      | head :: tail ->
        {
          NonEmptyList.Head = head
          Tail = tail
        }
        |> Some
    )

  interface ISimpleEventStorage<'e> with

    member this.GetStreamKey e = getStreamKey e

    member this.LoadEventRangeAcrossStreams (from: V, untilExcluding: V)=task{
      let! events = this.EventSet().OrderBy(_.Version).Where(fun x -> from <= x.Version && x.Version < untilExcluding).ToListAsync()
      return events |> Seq.map options.ToEvent |> Seq.toList
    }

    member this.LoadStream id =
      taskResult {
        let! stream = this.GetStream id
        let! stream =
          stream
          |> Result.requireSome (EventStoreError.New(EventStoreErrorDetails.NotFound, None))
        let stream = stream :> IStreamDbo
        return stream
      }

    member this.LoadEventRange(id: Guid, from, until) = this.LoadEventRange(id, from, until)

    member this.LoadAllEvents id = this.LoadEventRange(id, 0, 0)

    member this.Commit v =
      // printfn "commit\n%A" v
      let addStream v =
        taskResult {
          let streamDto =
            StreamDto<'e>(Id = v.Id, Created = v.TimeStamp, Modified = v.TimeStamp)

          this.StreamSet().Add streamDto |> ignore
        }
      let updateStream v =
        taskResult {
          let! stream = this.GetStream v.Id
          let! stream =
            stream
            |> Result.requireSome (EventStoreError.New(EventStoreErrorDetails.NotFound, None))
          stream.Modified <- v.TimeStamp
          stream.Version <- v.Version
        }

      taskResult {
        logger.debug (
          Log.setMessage "Committing events. Stream is new = {isNew}"
          >> Log.addParameter v.IsNew
          >> Log.addContext "Events" v.Events
        )
        if v.IsNew then do! addStream v else do! updateStream v
        let eventDtos = v.Events.List |> Seq.map options.ToEventStorageObject
        eventDtos |> Seq.iter (this.EventSet().Add >> ignore)
        logger.debug (
          Log.setMessage "Saving"
          >> Log.addContext "EfDebugView" db.ChangeTracker.DebugView.LongView
        )
        let! _ = db.SaveChangesAsync()
        let! result = db.SaveChangesAsync()
        logger.info (Log.setMessage "SaveChanges {count}" >> Log.addParameter result)
        return ()

      }

open Helpers

type ConfigureStreamHelper<'discriminator>
  (
    ty: ModelBuilder,
    streamEntityDiscriminator: Metadata.Builders.DiscriminatorBuilder<'discriminator>,
    eventEntityDiscriminator: Metadata.Builders.DiscriminatorBuilder<'discriminator>
  ) =
  member _.WithStreamType<'event>(discriminatorValue: 'discriminator, cfg) =
    streamEntityDiscriminator.HasValue<StreamDto<'event>> discriminatorValue
    |> ignore

    eventEntityDiscriminator.HasValue<EventDto<'event>> discriminatorValue |> ignore

    let eventEntity = ty.Entity<EventDto<'event>>()

    eventEntity.Property(fun x -> x.StreamId).IsRequired(true).HasColumnName "StreamId"
    |> ignore

    eventEntity.HasIndex(fun x -> (x.Version, x.StreamId) :> obj).IsUnique()
    |> ignore

    let dataProp =
      ty
        .Entity<EventDto<'event>>()
        .Property(fun x -> x.Data)
        .IsRequired(true)
        .HasColumnName("Data")
        .HasConversion(TinyEventStore.Json.serialize, TinyEventStore.Json.deserialize)

    // let headerProp =
    //   ty.Entity<StorableEvent<'id, 'event, 'header>>().Property(fun x -> x.Header)
    // headerProp.HasConversion(Json.serialize, Json.deserialize) |> ignore
    // headerProp.HasColumnName("Header") |> ignore

    ty
      .Entity<EventDto<'event>>()
      .HasOne(fun x -> x.Stream)
      .WithMany(fun x -> x.Children :> Collections.Generic.IEnumerable<EventDto<'event>>)
      .HasForeignKey(fun x -> x.StreamId :> obj)
      .HasConstraintName
      "stream_events"
    |> ignore

    cfg |> Option.iter (fun x -> x (eventEntity, dataProp))
    ()

type ModelBuilderExtensions() =
  [<Extension>]
  static member AddSharedEventStorage<'tDiscriminator>
    (
      ty: ModelBuilder,
      tablePrefix: string,
      // options: EventStorageOptions<'streamId, 'streamIdRaw, 'id, 'idraw>,
      // system: System<'state, 'e, 'ed, 'c>,
      configure: ConfigureStreamHelper<'tDiscriminator> -> unit
    ) =
    // Base Stream
    let _ =
      ty.Entity<StreamBaseDto>(fun entity ->
        entity.HasKey(fun x -> x.Id :> obj) |> ignore

        entity.ToTable(tablePrefix + "_streams") |> ignore

        entity.Property(fun x -> x.Id)
        // .HasConversion(toEfConverter (options.Converters.StreamId))
        |> ignore
      )

    // Base Event
    ty.Entity<EventBaseDto>(fun entity ->
      // entity.Property(fun x -> x.SequenceId).Ignore
      // entity.Ignore "SequenceId" |> ignore
      entity.HasKey(fun x -> x.Id :> obj) |> ignore

      entity.ToTable(tablePrefix + "_events") |> ignore
      entity.OwnsOne(fun x -> x.CausationId) |> ignore

      entity.Property(fun x -> x.CorrelationId).HasConversion(correlationIdConverter ())
      |> ignore

      entity.Ignore(fun x -> x.CorrelationId :> obj) |> ignore

      ()
    )
    |> ignore

    let streamEntity =
      ty.Entity<StreamBaseDto>().HasDiscriminator<'tDiscriminator> "Type"

    let eventEntity = ty.Entity<EventBaseDto>().HasDiscriminator<'tDiscriminator> "Type"
    configure (ConfigureStreamHelper<'tDiscriminator>(ty, streamEntity, eventEntity))
    ()
