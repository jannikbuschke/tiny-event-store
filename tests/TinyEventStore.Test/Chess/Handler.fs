module TinyEventStore.Test.Chess.Handler

open System
open System.Collections.Generic
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Logging
open TinyEventStore
open FsToolkit.ErrorHandling
open Microsoft.EntityFrameworkCore
open Microsoft.Extensions.DependencyInjection
open TinyEventStore.Test.Chess.Db

type Id = Chess.GameId
type Command = Chess.Command
type CommandHeader = Dictionary<string, obj>
type CommandEnvelope = CommandEnvelope<Id, Command,CommandHeader>
type Event = Chess.Event
type EventHeader = TinyEventStore.Test.Chess.Db.ChessEventHeader
type EventEnvelope = ChessEventEnvelope
type SideEffect = unit
type State = Chess.Game

let store =
  TinyEventStore.EfPure.efCreate<Id, State, Event, EventHeader, Command,CommandHeader, SideEffect, TinyEventStore.Test.Chess.Db.ChessDb>
    Chess.Game.Zero
    (fun state e -> Chess.evolve state e.Payload)
    (fun state command ->
      match Chess.decide state command.Payload with
      | Ok resultValue ->
        let eventEnvelopes = resultValue |> List.map (fun e -> e, EventHeader())
        Result.Ok(eventEnvelopes, [])
      | Error errorValue -> Result.Error errorValue)

let handleCommand (ctx: HttpContext) (streamId: Id, command: Command) =
  taskResult {
    let logger = ctx.RequestServices.GetService<ILogger<string>>()
    // let db = ctx.RequestServices.GetService<InvoicingDb>()
    let commandEnvelope: CommandEnvelope = CommandEnvelope.New(streamId, command,CommandHeader())
    let! runCommand = store.prepare ctx.RequestServices streamId
    let! commandResult = runCommand commandEnvelope
    store.updateEventStore2 ctx.RequestServices commandResult
    let db = store.getDb ctx.RequestServices
    let allEntries = db.ChangeTracker.Entries() |> Seq.toList

    db.ChangeTracker.Entries()
    |> Seq.iter (fun x -> (logger.LogInformation(sprintf "Entry %A" x)))

    let! result2 = db.SaveChangesAsync()
    printfn "Result %A" result2
    printfn "----"
    return ()
  }

let settingsStore =
  TinyEventStore.EfPure.efCreate<Id, Db.ChessSettings, SettingsEvent, EventHeader, SettingsCommand,CommandHeader, SideEffect, TinyEventStore.Test.Chess.Db.ChessDb>
    SettingsLogic.zero
    (fun state e -> SettingsLogic.evolve state e.Payload)
    (fun state command ->
      let events = SettingsLogic.handle state command.Payload |> List.map (fun e -> e, EventHeader())
      Result.Ok(events, []))

let handleSettingsCommand (ctx: HttpContext) (streamId: Id, command: SettingsCommand) =
  taskResult {
    let logger = ctx.RequestServices.GetService<ILogger<string>>()
    let commandEnvelope:  CommandEnvelope<Id,SettingsCommand,CommandHeader> = CommandEnvelope.New(streamId, command, CommandHeader())
    let! runCommand = settingsStore.prepare ctx.RequestServices streamId
    let! commandResult = runCommand commandEnvelope
    settingsStore.updateEventStore2 ctx.RequestServices commandResult
    let db = store.getDb ctx.RequestServices
    let allEntries = db.ChangeTracker.Entries() |> Seq.toList

    db.ChangeTracker.Entries()
    |> Seq.iter (fun x -> (logger.LogInformation(sprintf "Entry %A" x)))

    let! result2 = db.SaveChangesAsync()
    printfn "Result %A" result2
    printfn "----"
    return ()
  }
