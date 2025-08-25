module Theater2.Aggregate

open System
open TinyEventStore.InterfacesSimple

let evolve: TinyEventStore.Interfaces.Evolve<_, _> =
  fun state (e: TheaterEvent) ->
    match e.Details with
    | TheaterEventDetails.Created name ->
      {
        Name = name
        Modified = e.TimeStamp
        Created = e.TimeStamp
        IsDeleted = false
      }
    | TheaterEventDetails.Updated name ->
      { state with
          Name = name
          Modified = e.TimeStamp
      }
    | TheaterEventDetails.Deleted ->
      { state with
          IsDeleted = true
          Modified = e.TimeStamp
      }

let isDeleted state e = state.IsDeleted

let aggregate: TinyEventStore.InterfacesSimple.Aggregate<_, _> =
  {
    zero =
      {
        Name = null
        Modified = DateTimeOffset.MinValue
        Created = DateTimeOffset.MinValue
        IsDeleted = false
      }
    evolve = evolve
    isDeleting = isDeleted
  }

[<CLIMutable>]
type BackgroundListItem = {
    Id: Guid
    Name: string
  }

let evolveBackground: TinyEventStore.Interfaces.Evolve<BackgroundListItem, _> =
  fun state (e: TheaterEvent) ->
    match e.Details with
    | TheaterEventDetails.Created name ->
      {
        Id = e.StreamId
        Name = name + " bg"
      }
    | TheaterEventDetails.Updated name ->
      { state with
          Name = name + " bg"
      }
    | TheaterEventDetails.Deleted ->
      state

// let backgroundAggregate: TinyEventStore.InterfacesSimple.Aggregate<BackgroundListItem, _> = {
//     zero = {Id=Guid.Empty;Name=""}
//     evolve = evolveBackground
//     isDeleting = fun c s -> true
//   }

let backgroundProjectionDefinition: Projection<BackgroundListItem, TheaterEvent> = {
    zero = {Id=Guid.Empty;Name=""}
    evolve = evolveBackground
    isDeleting = fun c s -> false
    isInitializer = fun e -> e.Details.IsCreated
  }
