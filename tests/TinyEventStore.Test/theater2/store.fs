module Theater2.Store

open System
open TinyEventStore.InterfacesSimple
open TinyEventStore.EfSimpleStorage
open Microsoft.EntityFrameworkCore
open TinyEventStore.Json

type Discriminator =
  | Theater = 1

let efStorageOptions: EventStorageOptions<_> =
  {
    TableNamePrefix = "theater"
    ToEvent =
      fun dto ->
        {
          TheaterEvent.Id = dto.Id
          StreamId = dto.StreamId
          TimeStamp = dto.Timestamp
          Version = dto.Version
          Details = deserialize dto.Data
        }
    ToEventStorageObject =
      fun e ->
        Dtos.EventDto<TheaterEvent>(
          Id = e.Id,
          StreamId = e.StreamId,
          Data = serialize e.Details,
          Timestamp = e.TimeStamp,
          Version = e.Version
        )
  }

[<CLIMutable>]
type StateListItem =
  {
    Id: Guid
    Name: string
  }

type EventDbContext(options) =
  inherit DbContext(options)

  member this.ListItems() = this.Set<StateListItem>()

  override _.OnModelCreating(modelBuilder: ModelBuilder) : unit =
    modelBuilder.Entity<StateListItem>(fun entity ->
      entity.ToTable "list"
      |> ignore
      // entity.Property(_.Id).HasConversion(TheaterStreamId.ToRaw, TheaterStreamId.FromRaw)
      |> ignore
    )
    |> ignore

    let _ =
      modelBuilder.AddSharedEventStorage<Discriminator>(
        "theater_shared",
        fun o -> o.WithStreamType<TheaterEvent>(Discriminator.Theater, None)
      )
    ()
