module Theater.Decider

open TinyEventStore.Interfaces

let decideDetails state cmd =
  match cmd with
  | Create name -> TheaterEventDetails.Created name
  | Update name -> TheaterEventDetails.Updated name
  | Delete -> TheaterEventDetails.Deleted


let decide: TinyEventStore.Interfaces.Decide<_, _, _> =
  fun state cmd ->
    cmd.Data
    |> decideDetails state
    // |> fun x ->
    //     {
    //       Data = x
    //       Version = 0UL
    //       TimeStamp = cmd.TimeStamp
    //     }
    |> List.singleton

// let system: TinyEventStore.Interfaces.System<TheaterState, TheaterEvent, TheaterEventDetails, TheaterCommand> =
let system: TinyEventStore.Interfaces.System<_, TheaterEventEnvelope,TheaterEventDetails, _> =
  {
    aggregate = Aggregate.aggregate
    // projections = []
    decide = decide
    isCommandInitializer =
      fun c ->
        match c.Data with
        | Create _ -> true
        | _ -> false
    isEventInitialiser =
      fun e ->
        match e with
        | TheaterEventDetails.Created _ -> true
        | _ -> false

  }
