module Theater.Aggregate

open System

let evolve: TinyEventStore.Interfaces.Evolve<_, _> =
  fun state (e: TheaterEvent) ->
    match e.Data with
    | EventDetails.Created name ->
      { Name = name
        TimeStamp = e.TimeStamp
        IsDeleted = false }
    | EventDetails.Updated name ->
      { state with
          Name = name
          TimeStamp = e.TimeStamp }
    | EventDetails.Deleted ->
      { state with
          IsDeleted = true
          TimeStamp = e.TimeStamp }

let isDeleted state e = state.IsDeleted

let aggregate: TinyEventStore.Interfaces.Aggregate<_, _> =
  { zero =
      { Name = null
        TimeStamp = DateTimeOffset.MinValue
        IsDeleted = false }
    evolve = evolve
    isDeleted = isDeleted }
