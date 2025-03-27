module Theater.Decider

let decideDetails state cmd =
  match cmd with
  | Create name -> Created name
  | Update name -> Updated name
  | Delete -> Deleted


let decide: TinyEventStore.Interfaces.Decide<_, _, _> =
  fun state cmd ->
    cmd.Data
    |> decideDetails state
    |> fun x ->
        { Data = x
          Version = 0UL
          TimeStamp = cmd.TimeStamp }
    |> List.singleton

let system: TinyEventStore.Interfaces.System<_, _, _> =
  { aggregate = Aggregate.aggregate
    decide = decide }
