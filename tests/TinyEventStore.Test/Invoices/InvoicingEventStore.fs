module MyTestDomain.Invoicing.EventStore

open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Logging
open MyTestDomain.Invoicing.Core
open MyTestDomain.Invoicing.Db
open TinyEventStore
open FsToolkit.ErrorHandling
open Microsoft.Extensions.DependencyInjection

type Id = InvoiceId
type Command = Core.Command
type CommandEnvelope = CommandEnvelope<Id, Command, unit>
type Event = Core.Event
type EventHeader = Core.EventHeader
type EventEnvelope = Core.InvoiceEventEnvelope
type SideEffect = Core.SideEffect
type State = Projections.InvoiceData

let isNew (e: EventEnvelope<Id, Event, EventHeader>) =
  match e.Payload with
  | DraftCreated _ -> true
  | _ -> false

open System.Collections.Generic
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Logging
open TinyEventStore
open FsToolkit.ErrorHandling
open Microsoft.Extensions.DependencyInjection
open TinyEventStore.Ef.Store
open TinyEventStore.Ef.Projections
open Expecto
open MyTestDomain.Invoicing.Projections

let store=Configuration.Configure<Id, State, Event, EventHeader, Command, unit, InvoicingDb>(
  Projections.aggregate,
  CommandHandler.decide,
  []
  )
// let store =
//   Ef.LegacyStore.efCreate<Id, State, Event, EventHeader, Command, unit, SideEffect, InvoicingDb>
//     Projections.invoiceDefaultZero
//     Projections.invoiceDefaultEvolve
//     CommandHandler.decide


let handleCommand (ctx: HttpContext) (streamId: Id, command: Command) =
  taskResult {
    let logger = ctx.RequestServices.GetService<ILogger<string>>()
    let db = ctx.RequestServices.GetService<InvoicingDb>()

    let commandEnvelope = CommandEnvelope.New(streamId, command, ())

    let! runCommand = store.prepare ctx.RequestServices streamId
    let! commandResult = runCommand commandEnvelope

    store.updateEventStore2 ctx.RequestServices commandResult

    db.ChangeTracker.Entries()
    |> Seq.iter (fun x -> (logger.LogInformation(sprintf "Entry %A" x)))

    let! result2 = db.SaveChangesAsync()
    printfn "Result %A" result2
    printfn "----"
    return ()
  }
