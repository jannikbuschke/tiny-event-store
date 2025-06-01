module Theater2.Decider

open TinyEventStore.InterfacesSimple
open System

let decideDetails state cmd =
  match cmd with
  | Create name -> TheaterEventDetails.Created name
  | Update name -> TheaterEventDetails.Updated name
  | Delete -> TheaterEventDetails.Deleted

let decide: TinyEventStore.InterfacesSimple.Decide<_, _, _> =
  fun ctx state (cmd: TheaterCommand) ->
    if ctx.StreamId = Guid.Empty then
      failwith "streamid empty"

    cmd.Details
    |> decideDetails state
    |> fun details ->
        {
          TheaterEvent.Id = Guid.CreateVersion7 ctx.TimeStamp
          Details = details
          StreamId = ctx.StreamId
          TimeStamp = ctx.TimeStamp
          Version = V.increment ctx.Version
        }
    |> List.singleton
    |> NonEmptyList.From
// |> Ok

let system: TinyEventStore.InterfacesSimple.EventStoreDefinition<TheaterState, TheaterEvent, TheaterCommand> =
  {
    aggregate = Aggregate.aggregate
    // projections = []
    decide = decide
    // getEventVersion = fun x -> x.Version
    getEventVersion = _.Version
    isCommandInitializer =
      fun c ->
        match c.Details with
        | Create _ -> true
        | _ -> false
    isEventInitialiser =
      fun e ->
        match e.Details with
        | TheaterEventDetails.Created _ -> true
        | _ -> false

  }
