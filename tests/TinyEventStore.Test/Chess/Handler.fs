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
    | Error errorValue -> Result.Error errorValue)

let aggregate: Aggregate<Id, State, Event, EventHeader> =
  { zero = Chess.Game.Zero
    evolve = (fun state e -> Chess.evolve state e.Payload)
    shouldDelete = fun x y -> x.EventsChunk |> List.exists (fun e -> e.Payload = Event.Deleted) }

let listProjection =
  new EfProjection<Id, State, Event, EventHeader, ChessGameListItem, Db>(fun op ->
    { ChessGameListItem.Id = op.PreviousStream.Id
      IsFinished = false })

let store =
  Configuration.Configure<Id, State, Event, EventHeader, Command, CommandHeader, TinyEventStore.Test.Chess.Db.ChessDb>(
    aggregate,
    decide,
    // []
    [ listProjection ]
  )

let replay (ctx: HttpContext) (projection: EfProjection<Id, State, Event, EventHeader, ChessGameListItem, Db>) =
  taskResult { do! store.replayProjection ctx.RequestServices projection }
// let store =
//   TinyEventStore.EfEs.efCreate<
//     Id,
//     State,
//     Event,
//     EventHeader,
//     Command,
//     CommandHeader,
//     SideEffect,
//     TinyEventStore.Test.Chess.Db.ChessDb
//    >
//     Chess.Game.Zero
//     (fun state e -> Chess.evolve state e.Payload)
//     decide

let appendEvents (ctx: HttpContext) (streamId: Id, events: Event list) =
  taskResult {
    let events = events |> List.mapi (fun i e -> e, Dictionary())
    let! _ = store.applyEvents ctx.RequestServices (streamId, events)
    do! store.saveChangesAsync ctx.RequestServices
  // let! result = store.appendEvents ctx.RequestServices streamId events
  // store.updateEventStore2 ctx.RequestServices result
  // store.applyOperationResultToProjections ctx.RequestServices result
  // let db = store.getDb ctx.RequestServices
  //
  // let! dbResult = db.SaveChangesAsync()
  //
  // if dbResult = 0 then
  //   failwith "no changes applied to database"
  }

let handleGameCommand (ctx: HttpContext) (streamId: Id, command: Command) =
  taskResult {
    let logger = ctx.RequestServices.GetService<ILogger<string>>()

    let commandEnvelope: CommandEnvelope =
      CommandEnvelope.New(streamId, command, CommandHeader())

    let! _ = store.applyCommand ctx.RequestServices (streamId, commandEnvelope)
    do! store.saveChangesAsync ctx.RequestServices
    return ()
  }

let settingsStore =
  efCreate<Id, Db.ChessSettings, SettingsEvent, EventHeader, SettingsCommand, CommandHeader, SideEffect, ChessDb>
    SettingsLogic.zero
    (fun state e -> SettingsLogic.evolve state e.Payload)
    (fun state command ->
      let events =
        SettingsLogic.handle state command.Payload
        |> List.map (fun e -> e, EventHeader())

      Ok(events, []))

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
