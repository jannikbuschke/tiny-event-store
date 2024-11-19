module TinyEventStore.Test.Chess.Handler

open System.Collections.Generic
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Logging
open TinyEventStore
open FsToolkit.ErrorHandling
open Microsoft.Extensions.DependencyInjection
open TinyEventStore.Ef.Store
open TinyEventStore.Test.Chess.Db
open TinyEventStore.Ef.Projections

type Id = Chess.GameId
type Command = Chess.Command
type CommandHeader = Dictionary<string, obj>
type CommandEnvelope = CommandEnvelope<Id, Command, CommandHeader>
type Event = Chess.Event
type EventHeader = Db.ChessEventHeader
type EventEnvelope = ChessEventEnvelope
type SideEffect = unit
type State = Chess.Game
type Db = ChessDb

let decide =
  (fun state command ->
    match Chess.decide state command.Payload with
    | Ok resultValue ->
      let eventEnvelopes = resultValue |> List.map (fun e -> e, EventHeader())
      Ok(eventEnvelopes, [])
    | Error errorValue -> Error errorValue)

let aggregate: Aggregate<Id, State, Event, EventHeader> =
  { zero = Chess.Game.Zero
    evolve = (fun state e -> Chess.evolve state e.Payload)
    shouldDelete = fun x _ -> x.EventsChunk |> List.exists (fun e -> e.Payload = Event.Deleted) }

let asPgn (e: Chess.PieceMovement, color: Chess.Color, move: int) =
  let movement = e.Pgn()

  match color with
  | Chess.Color.White -> $"{move}. {movement}"
  | Chess.Color.Black -> $" {movement}"

let listProjection =
  new EfProjection<Id, State, Event, EventHeader, ChessGameListItem, Db>(fun op ->
    let history =
      op.New.EventsChunk
      |> List.fold
        (fun acc v ->
          let result =
            match v.Payload with
            | Event.PieceMoved m -> asPgn (m, Chess.Color.Black, 1)
            | _ -> ""

          acc + result)
        ""

    { ChessGameListItem.Id = op.PreviousStream.Id
      Version = op.New.Stream.Version
      History = history
      IsFinished = false })

let store =
  Configuration.Configure<Id, State, Event, EventHeader, Command, CommandHeader, ChessDb>(
    aggregate,
    decide,
    [ listProjection ]
  )

let replay (ctx: HttpContext) (projection: EfProjection<Id, State, Event, EventHeader, ChessGameListItem, Db>) =
  taskResult { do! store.replayProjection ctx.RequestServices projection }

let appendEvents (ctx: HttpContext) (streamId: Id, events: Event list) =
  taskResult {
    let events = events |> List.mapi (fun _ e -> e, Dictionary())
    let! result = store.applyEvents ctx.RequestServices (streamId, events)
    let! r2 = store.saveChangesAsyncWithResult ctx.RequestServices
    return ()
  }

let handleGameCommand (ctx: HttpContext) (streamId: Id, command: Command) =
  taskResult {
    let _ = ctx.RequestServices.GetService<ILogger<string>>()

    let commandEnvelope: CommandEnvelope =
      CommandEnvelope.New(streamId, command, CommandHeader())

    let! _ = store.applyCommand ctx.RequestServices (streamId, commandEnvelope)
    do! store.saveChangesAsync ctx.RequestServices
    return ()
  }

let settingsAggregate =
  { Aggregate.zero = SettingsLogic.zero
    evolve = (fun state e -> SettingsLogic.evolve state e.Payload)
    shouldDelete = fun _ _ -> failwith "Not Implemented" }

let settingsDecide =
  (fun state command ->
    let events =
      SettingsLogic.handle state command.Payload
      |> List.map (fun e -> e, EventHeader())

    Ok(events, []))

let settingsStore =
  Configuration.Configure<Id, Db.ChessSettings, SettingsEvent, EventHeader, SettingsCommand, CommandHeader, ChessDb>(
    settingsAggregate,
    settingsDecide,
    []
  )
// efCreate<>
//   (fun state command ->
//     let events =
//       SettingsLogic.handle state command.Payload
//       |> List.map (fun e -> e, EventHeader())
//
//     Ok(events, []))

let handleSettingsCommand (ctx: HttpContext) (streamId: Id, command: SettingsCommand) =
  taskResult {
    let logger = ctx.RequestServices.GetService<ILogger<string>>()

    let commandEnvelope: CommandEnvelope<Id, SettingsCommand, CommandHeader> =
      CommandEnvelope.New(streamId, command, CommandHeader())

    let! runCommand = settingsStore.prepare ctx.RequestServices streamId
    let! commandResult = runCommand commandEnvelope
    settingsStore.updateEventStore2 ctx.RequestServices commandResult
    let db = store.getDb ctx.RequestServices
    // let allEntries = db.ChangeTracker.Entries() |> Seq.toList

    db.ChangeTracker.Entries()
    |> Seq.iter (fun x -> (logger.LogInformation(sprintf "Entry %A" x)))

    let! result2 = db.SaveChangesAsync()
    printfn "Result %A" result2
    printfn "----"
    return ()
  }
