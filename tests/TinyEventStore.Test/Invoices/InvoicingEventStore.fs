module MyDomain.Invoicing.EventStore

open System
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Logging
open MyDomain.Invoicing.Core
open MyDomain.Invoicing.Db
open MyDomain.Invoicing.Projections
open TinyEventStore
open FsToolkit.ErrorHandling
open Microsoft.EntityFrameworkCore

type Id = InvoiceId
type Command = MyDomain.Invoicing.Core.Command
type CommandEnvelope = CommandEnvelope<Id, Command>
type Event = MyDomain.Invoicing.Core.Event
type EventHeader = MyDomain.Invoicing.Core.EventHeader
type EventEnvelope = Core.InvoiceEventEnvelope
type SideEffect = MyDomain.Invoicing.Core.SideEffect
type State = MyDomain.Invoicing.Projections.InvoiceData

open Microsoft.Extensions.DependencyInjection

// run only with the latest events and state

let updateEntity (db: DbContext) state events isNew =
  let entry = db.Entry(state)

  let isNew = events |> List.exists isNew

  entry.State <- if isNew then EntityState.Added else EntityState.Modified

  Threading.Tasks.Task.FromResult(Result.Ok())

let isNew (e: EventEnvelope<Id, Event, EventHeader>) =
  match e.Payload with
  | Event.DraftCreated _ -> true
  | _ -> false

let store = TinyEventStore.EfPure.efCreate<Id, State, Event, EventHeader, Command, SideEffect,InvoicingDb> Projections.invoiceDefaultZero Projections.invoiceDefaultEvolve CommandHandler.decide
// let commandHandler (ctx: IServiceProvider) =
//   let commandhandler =
//     efCommandHandler<InvoiceId, InvoiceData, Command, Event, EventHeader, SideEffect, InvoicingDb>
//       Projections.invoiceDefaultZero
//       Projections.invoiceDefaultEvolve
//       CommandHandler.decide
//       ctx
//
//   commandhandler

let updateStream logger (db: InvoicingDb) (stream: Stream<'id, 'event, 'header>) =
  let savedStream = db.Find<Stream<'id, 'event, 'header>>(stream.Id)
  let entry = db.Entry(savedStream)
  entry.CurrentValues.SetValues stream

  ()

let instertStream logger (db: InvoicingDb) (stream: Stream<'id, 'event, 'header>) =
  let entry = db.Entry(stream)

  db.Set<Stream<'id, 'event, 'header>>().Add(stream) |> ignore
  // entry.State <- EntityState.Added
  ()

let insertOrUpdateStream
  logger
  (db: InvoicingDb)
  (stream: Stream<'id, 'event, 'header>)
  (newEvents: InvoiceEventEnvelope list)
  =
  let isNew =
    newEvents
    |> Seq.exists (fun x ->
      x.Payload
      |> function
        | Event.DraftCreated _ -> true
        | _ -> false)

  newEvents |> List.iter (db.Add >> ignore)

  if isNew then
    instertStream logger db stream
  else
    updateStream logger db stream

let handleCommand (ctx: HttpContext) (streamId: Id, command: Command) =
  taskResult {
    let logger = ctx.RequestServices.GetService<ILogger<string>>()
    let db = ctx.RequestServices.GetService<InvoicingDb>()

    let commandEnvelope: CommandEnvelope = CommandEnvelope.New(streamId, command)

    let! runCommand = store.decide ctx.RequestServices streamId
    let! commandResult = runCommand commandEnvelope

    store.updateEventStore ctx.RequestServices commandResult

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
