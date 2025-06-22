module rec Theater2.Store

open System
open TinyEventStore.EfSimpleStorage
open Microsoft.EntityFrameworkCore
open TinyEventStore.Json
open TinyEventStore.InterfacesSimple.Core
open Decider
open Microsoft.Extensions.DependencyInjection
open TinyEventStore.Check
open Expecto
open System
open Decider
open TinyEventStore.InterfacesSimple
open TinyEventStore.EfSimpleStorage
open Microsoft.EntityFrameworkCore
open Microsoft.Extensions.DependencyInjection
open System.IO
open TinyEventStore.Simple

type Discriminator =
  | Theater = 1

let efStorageOptions: EventStorageOptions<TheaterEvent> =
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
        Dtos.EventDto<_>(
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

let printSubscription: Subscription<_, _, _> =
  fun _ x ->
    task {
      printfn "subscription %A" x
      return ()
    }

let deriveListItem (eventResult: AppendEventsResult<TheaterState, _>) =
  {
    Id = eventResult.Id
    Name = eventResult.State.Name
  }
let listItemProjection: Projection.DeriveProjection<_, TheaterEvent, _, _> =
  {
    Derive = deriveListItem
    ShouldDelete = fun x -> x.Events.List |> List.exists _.Details.IsDeleted
  }
let immediateProjectionsSubscription: OnCommittingEventHandler<_, _, _> =
  fun (ctx: IServiceProvider) x ->
    task {
      let db = ctx.GetRequiredService<EventDbContext>()
      do! Projection.handler db x listItemProjection
    }

let onCommitting = [ immediateProjectionsSubscription ]

let handler ctx = createHandler system onCommitting ctx

type EventDbContext(options, serviceProvider: IServiceProvider) =
  inherit DbContext(options)

  member _.EventStore() = handler serviceProvider

  member this.TheaterEventStorage() =
    EfSimpleStorage<TheaterState, TheaterEvent, TheaterCommand, EventDbContext>(this, efStorageOptions)

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
