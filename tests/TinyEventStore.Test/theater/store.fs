module Theater.Store

open System
open TinyEventStore.Interfaces
open TinyEventStore.EfStorage
open Microsoft.EntityFrameworkCore

[<RequireQualifiedAccess>]
type TheaterStreamId =
  | TheaterStreamId of Guid

  static member New() = TheaterStreamId(Guid.NewGuid())
  static member ToRaw(TheaterStreamId id) = id
  static member FromRaw(id) = TheaterStreamId(id)
  static member FromRawString(input: string) = input |> Guid.Parse |> TheaterStreamId
  static member Converter = TheaterStreamId.ToRaw, TheaterStreamId.FromRaw

[<RequireQualifiedAccess>]
type TheaterEventId =
  | TheaterEventId of Guid

  static member New() = TheaterEventId(Guid.NewGuid())
  static member ToRaw(TheaterEventId id) = id
  static member FromRaw(id) = TheaterEventId(id)
  static member Converter = TheaterEventId.ToRaw, TheaterEventId.FromRaw

let createStream: StreamCreator<_, _, _, _> =
  fun appendEventsResult ->
    {
      Version = appendEventsResult.Version
      Created = appendEventsResult.TimeStamp
      Updated = appendEventsResult.TimeStamp
      Name = ""
    }

let updateStream: StreamUpdater<_, _, _, _> =
  fun stream0 appendEventsResult ->
    { stream0 with
        Version = appendEventsResult.Version
        Updated = appendEventsResult.TimeStamp
        Name = ""
    }

// let inmemStorage =
//   // InmemEventStorage.newStorage<Guid, TheaterStream, TheaterState, TheaterEvent, TheaterCommand> (
//   InmemEventStorage.newStorage<_, _, _, _, _> (createStream, updateStream)

type Discriminator =
  | Theater = 1

let efStorageOptions: EventStorageOptions<_, _, _, TheaterState, _> =
  {
    StreamId = TheaterStreamId.Converter
    Stream =
      (fun (stream, id, version) -> Dtos.StreamDto(Id = (id |> TheaterStreamId.ToRaw), Version = version)),
      (fun x ->
        {
          TheaterStream.Version = x.Version
          Created = x.Created
          Updated = x.Modified
          Name = ""
        },
        x.Id |> TheaterStreamId.FromRaw,
        x.Version
      )
    Event =
      (fun (event: TheaterEvent, id, version) ->
        Dtos.EventDto(
          StreamId = (id |> TheaterStreamId.ToRaw),
          Version = version,
          Data = event,
          Timestamp = event.TimeStamp
        )
      ),
      (fun x ->
        {
          TheaterEvent.Version = x.Version
          TimeStamp = x.Timestamp
          Data = x.Data.Data
        },
        x.StreamId |> TheaterStreamId.FromRaw,
        x.Version
      )
    TableNamePrefix = "theater"
    CreateStream = createStream
    UpdateStream = updateStream
  }

[<CLIMutable>]
type StateListItem =
  {
    Id: TheaterStreamId
    Name: string
  }

module Db =
  let addEntity (modelBuilder: ModelBuilder) = ()


type EventDbContext(options) =
  inherit DbContext(options)

  member this.ListItems() = this.Set<StateListItem>()

  // member this.GetStorage(system: System<_, _, _, _>) = ()
  override _.OnModelCreating(modelBuilder: ModelBuilder) : unit =
    modelBuilder.Entity<StateListItem>(fun entity ->
      entity.ToTable("list") |> ignore
      entity.Property(_.Id).HasConversion(TheaterStreamId.ToRaw, TheaterStreamId.FromRaw)
      |> ignore
    )
    |> ignore

    let _ =
      modelBuilder.AddSharedEventStorage<Guid, Guid, Discriminator>(
        "theater_shared",
        fun o ->
          o.WithStreamType<TheaterStreamId, Guid, TheaterEventId, Guid, TheaterStream, TheaterEvent>(
            Discriminator.Theater,
            None
          )
      )
    ()
