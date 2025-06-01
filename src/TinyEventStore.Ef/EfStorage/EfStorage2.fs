namespace TinyEventStore.EfStorage2

open TinyEventStore.Interfaces
open FsToolkit.ErrorHandling
open System
open Microsoft.EntityFrameworkCore
open System.Linq
open System.Runtime.CompilerServices
open Core
open Microsoft.EntityFrameworkCore.Storage.ValueConversion
open TinyEventStore.EfStorage

type Converter<'t, 'traw> = ('t -> 'traw) * ('traw -> 't)

type EventStorageOptions<'streamId, 'streamIdRaw, 'stream, 'event when 'streamId: equality and 'streamIdRaw: equality> =
  {
    TableNamePrefix: string
    StreamId: Converter<'streamId, 'streamIdRaw>
    Stream: Converter<'stream * 'streamId * V, Dtos.StreamDto<'streamIdRaw, 'event>>
    Event: Converter<'event * 'streamId * V, Dtos.EventDto<'streamIdRaw, 'event>>
  }

module private Helpers =

  let correlationIdConverter () =
    let toRaw = Option.map TinyEventStore.CorrelationId.ToRawValue >> Option.toNullable
    let fromRaw =
      Option.ofNullable >> Option.map TinyEventStore.CorrelationId.FromRawValue
    ValueConverter<TinyEventStore.CorrelationId option, Nullable<Guid>>(toRaw, fromRaw)

module Seq =
  let toSeqAsync (q: IQueryable<'t>) = q.ToListAsync()

type EfStorage<'streamId, 'streamIdRaw, 'stream, 'state, 'event, 'c, 'db
  when 'stream :> IStream
  and 'event :> IEvent
  and
  // and 'event: not struct
  'stream: not struct
  and 'streamId: equality
  and 'streamIdRaw: equality>
  (
    streamCreator: StreamCreator<'streamId, 'stream, _, _>,
    streamUpdater: StreamUpdater<'streamId, 'stream, _, _>,
    db: DbContext,
    options: EventStorageOptions<'streamId, 'streamIdRaw, _, _>
  ) =

  let eventConverter = options.Event
  let streamConverter = options.Stream
  let idConverter = options.StreamId

  let toEvent dto =
    dto |> (eventConverter |> snd) |> (fun (x, _, _) -> x)

  let toStream dto =
    dto |> (streamConverter |> snd) |> (fun (x, _, _) -> x)

  let toStreamDto stream = stream |> (streamConverter |> fst)

  let toEventDto event = event |> (eventConverter |> fst)

  let converToList events = events |> Seq.map toEvent |> Seq.toList

  member _.StreamSet() =
    // printfn "Stream %A" (typeof<'streamIdRaw>)
    db.Set<Dtos.StreamDto<'streamIdRaw, 'event>>()

  member this.QueryStreams() = this.StreamSet().AsNoTracking()
  member this.GetStream(id) =
    task {
      let streamId = id |> (idConverter |> fst)
      let! stream = this.StreamSet().AsNoTracking().FirstOrDefaultAsync(fun x -> x.Id = streamId)
      return stream
    }

  member _.EventSet() =
    db.Set<Dtos.EventDto<'streamIdRaw, 'event>>()

  member this.QueryEvents() =
    this.EventSet().OrderBy(fun x -> x.Version).AsNoTracking()

  member this.QueryStreamEvents(id: 'streamId) =
    let streamId = id |> (idConverter |> fst)
    this.QueryEvents().Where(fun x -> x.StreamId = streamId).AsNoTracking()


  interface IEventStorage<'streamId, 'stream, 'state, 'event, 'c> with

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
    // task {
    //   let! e = this.QueryStreamEvents(id).ToListAsync()
    //   return e |> Seq.map toEvent |> Seq.toList |> Some
    // }

    member this.LoadAllEvents(id) =
      id
      |> this.QueryStreamEvents
      |> Seq.toSeqAsync
      |> Task.map (converToList >> Some)
    // task {
    //   let! e = this.QueryStreamEvents(id).ToListAsync()
    //   return e |> Seq.map toEvent |> Seq.toList |> Some
    // }

    member this.LoadRequiredStream(id: 'streamId) =
      task {
        let! stream = this.GetStream id
        if box stream = null then
          return Error(EventStoreError.New(EventStoreErrorDetails.NotFound, None))
        else
          let! events = this.QueryStreamEvents(id).ToListAsync()
          let events = events |> Seq.map toEvent |> Seq.toList
          return Ok(events, stream |> toStream)
      }

    member this.LoadStream(id: 'streamId) =
      task {
        let! x = (this :> IEventStorage<_, _, _, _, _>).LoadRequiredStream id
        return x |> Option.ofResult
      }

    member this.Commit(v) =
      taskResult {
        let! stream = (this :> IEventStorage<_, _, _, _, _>).LoadStream v.Id

        let! s1 =
          if v.IsNew then
            streamCreator v |> Ok
          else
            stream
            |> Option.map (fun (_, existingStream) -> streamUpdater existingStream v |> Ok)
            |> Option.defaultValue (EventStoreError.New(EventStoreErrorDetails.NotFound, None) |> Error)
        let streamDto = (s1, v.Id, s1.Version) |> toStreamDto
        if v.IsNew then
          this.StreamSet().Add streamDto |> ignore
        else
          let existingEntry =
            this.StreamSet().Local.FirstOrDefault(fun x -> x.Id = streamDto.Id)
          if box existingEntry = null then
            this.StreamSet().Attach streamDto |> ignore
          else
            (this.StreamSet().Entry existingEntry).CurrentValues.SetValues streamDto
            |> ignore

        let eventDtos = v.Events |> Seq.map (fun x -> toEventDto (x, v.Id, x.Version))

        eventDtos |> Seq.iter (this.EventSet().Add >> ignore)

        let! _ = db.SaveChangesAsync()

        return ()
      }

  member this.LoadAllEvents(id) =
    (this :> IEventStorage<_, _, _, _, _>).LoadAllEvents id


open Helpers

type ConfigureStreamHelper<'streamIdRaw, 'eventIdRaw, 'discriminator when 'streamIdRaw: equality>
  (
    ty: ModelBuilder,
    streamEntityDiscriminator: Metadata.Builders.DiscriminatorBuilder<'discriminator>,
    eventEntityDiscriminator: Metadata.Builders.DiscriminatorBuilder<'discriminator>
  ) =
  member _.WithStreamType<'streamId, 'streamIdRaw, 'eventId, 'eventIdRaw, 'stream, 'event
    when 'eventId: equality and 'streamId: equality>
    (discriminatorValue: 'discriminator, options: EventStorageOptions<'streamId, 'streamIdRaw, 'stream, 'event>, cfg) =

    streamEntityDiscriminator.HasValue<Dtos.StreamDto<'streamIdRaw, 'event>> discriminatorValue
    |> ignore

    eventEntityDiscriminator.HasValue<Dtos.EventDto<'streamIdRaw, 'event>> discriminatorValue
    |> ignore

    let eventEntity = ty.Entity<Dtos.EventDto<'streamIdRaw, 'event>>()

    eventEntity
      .Property(fun x -> x.StreamId)
      .IsRequired(true)
      .HasColumnName("StreamId")
    // .HasConversion(toEfConverter options.StreamId)
    |> ignore

    eventEntity.HasIndex(fun x -> (x.Version, x.StreamId) :> obj).IsUnique()
    |> ignore

    let dataProp =
      ty
        .Entity<Dtos.EventDto<'streamIdRaw, 'event>>()
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
      .Entity<Dtos.EventDto<'streamIdRaw, 'event>>()
      .HasOne(fun x -> x.Stream)
      .WithMany(fun x -> x.Children :> Collections.Generic.IEnumerable<Dtos.EventDto<'streamIdRaw, 'event>>)
      .HasForeignKey(fun x -> x.StreamId :> obj)
      .HasConstraintName("stream_events")
    |> ignore

    cfg |> Option.iter (fun x -> x (eventEntity, dataProp))
    ()

// member this.WithStreamType<'event>(name: 'tDiscriminator) = this.WithStreamType<'event>(name, None)

[<Extension>]
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
    let abstractStream =
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

      entity.Property(fun x -> x.EventId).IsRequired(true)
      // .HasConversion(toEfConverter options.Converters.Id)
      |> ignore

      entity.ToTable(tablePrefix + "_events") |> ignore
      entity.OwnsOne(fun x -> x.CausationId) |> ignore

      entity
        .Property(fun x -> x.CorrelationId)
        .HasConversion(correlationIdConverter ())
      |> ignore

      entity.Ignore(fun x -> x.CorrelationId :> obj) |> ignore

      ()
    )
    |> ignore

    let streamEntity =
      ty
        .Entity<Dtos.StreamBaseDto<'streamIdRaw>>()
        .HasDiscriminator<'tDiscriminator>("Type")

    let eventEntity =
      ty
        .Entity<Dtos.EventBaseDto<'eventIdRaw>>()
        .HasDiscriminator<'tDiscriminator>("Type")
    configure (ConfigureStreamHelper<'streamIdRaw, 'eventIdRaw, 'tDiscriminator>(ty, streamEntity, eventEntity))
    ()

// [<Extension>]
// static member AddSharedEventStorage<'tDiscriminator>
//   (
//     ty: ModelBuilder,
//     tablePrefix: string,
//     // options: EventStorageOptions<'streamId, 'streamIdRaw, 'id, 'idraw>,
//     // system: System<'state, 'e, 'ed, 'c>,
//     configure: ConfigureStreamHelper<Guid, Guid, 'tDiscriminator> -> unit
//   ) =
//   ty.AddSharedEventStorage<Guid, Guid, 'tDiscriminator>(tablePrefix, configure)
