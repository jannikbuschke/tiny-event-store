module Theater2.Aggregate

open System

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
