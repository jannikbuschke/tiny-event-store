module MyTestDomain.Invoicing.EventStore

open System
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Logging
open MyTestDomain.Invoicing.Core
open MyTestDomain.Invoicing.Db
open TinyEventStore
open FsToolkit.ErrorHandling
open Microsoft.EntityFrameworkCore
open Microsoft.Extensions.DependencyInjection

type Id = InvoiceId
type Command = MyTestDomain.Invoicing.Core.Command
type CommandEnvelope = CommandEnvelope<Id, Command,unit>
type Event = MyTestDomain.Invoicing.Core.Event
type EventHeader = MyTestDomain.Invoicing.Core.EventHeader
type EventEnvelope = Core.InvoiceEventEnvelope
type SideEffect = MyTestDomain.Invoicing.Core.SideEffect
type State = MyTestDomain.Invoicing.Projections.InvoiceData


// let updateEntity (db: DbContext) state events isNew =
//   let entry = db.Entry(state)
//
//   let isNew = events |> List.exists isNew
//
//   entry.State <- if isNew then EntityState.Added else EntityState.Modified
//
//   Threading.Tasks.Task.FromResult(Result.Ok())

let isNew (e: EventEnvelope<Id, Event, EventHeader>) =
  match e.Payload with
  | Event.DraftCreated _ -> true
  | _ -> false

let store =
  TinyEventStore.EfEs.efCreate<Id, State, Event, EventHeader, Command,unit, SideEffect, InvoicingDb> Projections.invoiceDefaultZero Projections.invoiceDefaultEvolve CommandHandler.decide

// let updateStream logger (db: InvoicingDb) (stream: Stream<'id, 'event, 'header>) =
//   let savedStream = db.Find<Stream<'id, 'event, 'header>>(stream.Id)
//   let entry = db.Entry(savedStream)
//   entry.CurrentValues.SetValues stream
//   ()
//
// let instertStream logger (db: InvoicingDb) (stream: Stream<'id, 'event, 'header>) =
//   let entry = db.Entry(stream)
//   db.Set<Stream<'id, 'event, 'header>>().Add(stream) |> ignore
//   ()
//
// let insertOrUpdateStream
//   logger
//   (db: InvoicingDb)
//   (stream: Stream<'id, 'event, 'header>)
//   (newEvents: InvoiceEventEnvelope list)
//   =
//   let isNew =
//     newEvents
//     |> Seq.exists (fun x ->
//       x.Payload
//       |> function
//         | Event.DraftCreated _ -> true
//         | _ -> false)
//
//   newEvents |> List.iter (db.Add >> ignore)
//
//   if isNew then
//     instertStream logger db stream
//   else
//     updateStream logger db stream

let handleCommand (ctx: HttpContext) (streamId: Id, command: Command) =
  taskResult {
    let logger = ctx.RequestServices.GetService<ILogger<string>>()
    let db = ctx.RequestServices.GetService<InvoicingDb>()

    let commandEnvelope: CommandEnvelope = CommandEnvelope.New(streamId, command, ())

    let! runCommand = store.prepare ctx.RequestServices streamId
    let! commandResult = runCommand commandEnvelope

    store.updateEventStore2 ctx.RequestServices commandResult

    let allEntries = db.ChangeTracker.Entries() |> Seq.toList

    let entries =
      db.ChangeTracker.Entries()
      |> Seq.filter (fun x -> x.State = EntityState.Added)
      |> Seq.toList

    db.ChangeTracker.Entries()
    |> Seq.iter (fun x -> (logger.LogInformation(sprintf "Entry %A" x)))

    let! result2 = db.SaveChangesAsync()
    printfn "Result %A" result2
    printfn "----"
    return ()
  }
