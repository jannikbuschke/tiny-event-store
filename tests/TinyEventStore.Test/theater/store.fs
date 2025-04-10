module Theater.Store

open System
open TinyEventStore.Interfaces
open TinyEventStore.EfStorage
open Microsoft.EntityFrameworkCore

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

type Discriminator =
  | Theater = 1

let efStorageOptions: EventStorageOptions<_, _> =
  {
    StreamId = TheaterStreamId.Converter
    // Stream =
    //   (fun (stream, id, version) ->
    //     // Dtos.StreamDto.Create(id |> TheaterStreamId.ToRaw, version,false,stream.Created,stream.Updated,Children=[])),
    //     Dtos.StreamDto(Id = (id |> TheaterStreamId.ToRaw), Version = version)),
    //   (fun x ->
    //     {
    //       TheaterStream.Version = x.Version
    //       Created = x.Created
    //       Updated = x.Modified
    //       Name = ""
    //     },
    //     x.Id |> TheaterStreamId.FromRaw,
    //     x.Version
    //   )
    // Event =
    //   (fun (event: TheaterEventDetails, id, version) ->
    //     Dtos.EventDto(
    //       StreamId = (id |> TheaterStreamId.ToRaw),
    //       Version = version,
    //       Data = event,
    //       Timestamp = event.TimeStamp
    //     )
    //   ),
    //   (fun x ->
    //     {
    //       TheaterEvent.Version = x.Version
    //       EventId = x.EventId
    //       StreamId = x.StreamId |> TheaterStreamId.FromRaw
    //       TimeStamp = x.Timestamp
    //       Data = x.Data.Data
    //     },
    //     x.StreamId |> TheaterStreamId.FromRaw,
    //     x.Version
    //   )
    TableNamePrefix = "theater"
    // CreateStream = createStream
    // UpdateStream = updateStream
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

  override _.OnModelCreating(modelBuilder: ModelBuilder) : unit =
    modelBuilder.Entity<StateListItem>(fun entity ->
      entity.ToTable "list" |> ignore
      entity.Property(_.Id).HasConversion(TheaterStreamId.ToRaw, TheaterStreamId.FromRaw)
      |> ignore
    )
    |> ignore

    let _ =
      modelBuilder.AddSharedEventStorage<Guid, Guid, Discriminator>(
        "theater_shared",
        fun o ->
          o.WithStreamType<TheaterStreamId, Guid,   TheaterStream, TheaterEventDetails>(
            Discriminator.Theater,
            None
          )
      )
    ()
