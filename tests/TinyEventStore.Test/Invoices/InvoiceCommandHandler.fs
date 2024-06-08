module MyDomain.Invoicing.CommandHandler

open System
open Microsoft.AspNetCore.Http
open MyDomain
open MyDomain.Invoicing.Core
open MyDomain.Invoicing.Db
open MyDomain.Invoicing.Projections
open TinyEventStore
open FsToolkit.ErrorHandling
open Microsoft.Extensions.DependencyInjection

type Command = MyDomain.Invoicing.Core.Command
type CommandEnvelope = CommandEnvelope<Guid, Command,unit>
type Event = MyDomain.Invoicing.Core.Event
type EventHeader = MyDomain.Invoicing.Core.EventHeader
type SideEffect = MyDomain.Invoicing.Core.SideEffect
type State = MyDomain.Invoicing.Projections.InvoiceData

let validateDraftPosition (position: DraftPosition) : Result<InvoicePosition, string> =
  result {
    let! description = position.Description |> Result.requireSome "Description is required"

    let! quantity = position.Quantity |> Result.requireSome "Quantity is required"

    let! price = position.Price |> Result.requireSome "Price is required"

    return
      { InvoicePosition.Description = description
        Quantity = quantity
        Price = price }
  }

let finaliseDraft (draft: InvoiceDraft) : Result<Invoice, string> =
  result {
    let! customerId = draft.CustomerId |> Result.requireSome "Customerid is required"
    // let! dueDate = draft.DueDate |> Result.requireSome ("DueDate is required")
    let! number = draft.InvoiceNumber |> Result.requireSome "InvoiceNumber is required"

    let! positions =
      draft.Positions
      |> List.traverseResultA validateDraftPosition
      |> Result.mapError (fun e -> e |> String.concat ", ")
    // let positions = positions |> List.traverseResultA
    return
      { Invoice.InvoiceNumber = number
        CustomerId = customerId
        CustomerName = ""
        Positions = positions }
  }

let decide: PureDecide<Id, State, Command,unit, Event, EventHeader, SideEffect> =
  // TODO: get from auth
  let userId = UserId.New()

  fun (state) (command) ->
    let header: EventHeader = { UserId = userId }

    match command.Payload with
    | Command.UpdateDraft draft -> Result.Ok([ Event.DraftUpdated draft, header ], [])
    | Command.CreateDraft draft -> Result.Ok([ Event.DraftCreated draft, header ], [])
    | Command.Finalize ->
      match state with
      | InvoiceData.Draft draft ->
        result {
          let! invoice = finaliseDraft draft
          return ([ Event.Finalized invoice, header ], [])
        }
      | _ -> failwith "invalid state"

    | Command.MarkAsPaid -> failwith "not yet implemented"
