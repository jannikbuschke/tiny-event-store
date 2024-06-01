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
type State = MyDomain.Invoicing.Projections.InvoiceProjection

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

// let handlers (ctx: HttpContext) : InlineEventHandler<Id, State, Event, EventHeader> list =
//   let db = ctx.RequestServices.GetService<InvoicingDb>()
//   let updateEntity = updateEntity db
//
//   [ fun (state, events) -> updateEntity state events isNew ]

let commandHandler (ctx: IServiceProvider) =
  let commandhandler =
    efCommandHandler<InvoiceId, InvoiceData, Command, Event, EventHeader, SideEffect, InvoicingDb>
      Projections.invoiceDefaultZero
      Projections.invoiceDefaultEvolve
      CommandHandler.decide
      ctx

  commandhandler

let updateProjection<'id, 'a when 'a: not struct>
  (id: 'id)
  (logger: ILogger)
  (db: InvoicingDb)
  (events: EventEnvelope seq)
  evolve
  zero
  =
  let state: 'a = events |> Seq.fold evolve zero

  logger.LogInformation(sprintf "Projectionstate %A" state)

  let x: DefaultProjection<'id, 'a> =
    { Id = id
      Value = state
      Created = DateTimeOffset.UtcNow
      Updated = DateTimeOffset.UtcNow }

  let entry = db.Set<DefaultProjection<'id, 'a>>().Update(x)
  entry.Property(fun x -> x.Created).IsModified <- false
  ()

let createProjection<'id, 'a when 'a: not struct>
  (id: 'id)
  (logger: ILogger)
  (db: InvoicingDb)
  (events: EventEnvelope seq)
  evolve
  zero
  =
  let state: 'a = events |> Seq.fold evolve zero

  logger.LogInformation(sprintf "Projectionstate %A" state)

  let x: DefaultProjection<'id, 'a> =
    { Id = id
      Value = state
      Created = DateTimeOffset.UtcNow
      Updated = DateTimeOffset.UtcNow }

  db.Set<DefaultProjection<'id, 'a>>().Add(x) |> ignore

  ()

let updateInvoiceListProjection
  logger
  (db: InvoicingDb)
  (stream: Stream<InvoiceId, Event, EventHeader>)
  (events: EventEnvelope list)
  =
  let isNew =
    events
    |> List.exists (fun x ->
      x.Payload
      |> function
        | Event.DraftCreated _ -> true
        | _ -> false)

  if isNew then
    createProjection stream.Id logger db stream.Events Projections.invoiceListEvolve Projections.invoiceListZero
  else
    updateProjection stream.Id logger db stream.Events Projections.invoiceListEvolve Projections.invoiceListZero

  ()

let updateDefaultProjection
  logger
  (db: InvoicingDb)
  (stream: Stream<InvoiceId, Event, EventHeader>)
  (events: EventEnvelope list)
  =
  let isNew =
    events
    |> List.exists (fun x ->
      x.Payload
      |> function
        | Event.DraftCreated _ -> true
        | _ -> false)

  if isNew then
    createProjection stream.Id logger db stream.Events Projections.invoiceDefaultEvolve Projections.invoiceDefaultZero
  else
    updateProjection stream.Id logger db stream.Events Projections.invoiceDefaultEvolve Projections.invoiceDefaultZero

  ()

let updateStream logger (db: InvoicingDb) (stream: Stream<'id, 'event, 'header>) =
  let savedStream = db.Find<Stream<'id, 'event, 'header>>(stream.Id)
  let entry = db.Entry(savedStream)
  entry.CurrentValues.SetValues stream

  // db.Set<Stream<'id,'event,'header>>()
  // db.Remove(entry) |> ignore
  //
  // db.Add(stream) |> ignore

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

    let! newState, newStream, newEvents, sideEffects, _ = commandHandler ctx.RequestServices commandEnvelope
    logger.LogInformation(sprintf "result %A %A" newEvents newState)
    printfn "result %A" result

    updateInvoiceListProjection logger db newStream newEvents
    updateDefaultProjection logger db newStream newEvents
    insertOrUpdateStream logger db newStream newEvents
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
