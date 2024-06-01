module MyDomain.Invoicing.Projections

open Core
open System
open TinyEventStore

[<RequireQualifiedAccess>]
type InvoiceData =
  | Draft of InvoiceDraft
  | Final of Invoice
  | Sent of Invoice * SendAt: DateTimeOffset * DueDate: DateTimeOffset
  | Paid of Invoice * PaidAt: DateTimeOffset * Note: string option

[<CLIMutable>]
type InvoiceProjection =
  { Data: InvoiceData
    // this seems to be metadata, that is usually useful
    Id: InvoiceId
    Created: DateTimeOffset
    Updated: DateTimeOffset }

  member this.Due =
    match this.Data with
    | InvoiceData.Sent(invoice, _, dueDate) -> dueDate < DateTimeOffset.UtcNow
    | _ -> false

// interface IProjectionState

let invoiceDefaultZero: InvoiceData =
  InvoiceData.Draft
    { InvoiceNumber = None
      CustomerId = None
      Positions = [] }

let invoiceDefaultEvolve =
  fun (state: InvoiceData) (event: InvoiceEventEnvelope) ->
    match event.Payload with
    | DraftCreated data -> InvoiceData.Draft data
    | DraftUpdated data -> InvoiceData.Draft data
    | Finalized data -> InvoiceData.Final data

[<RequireQualifiedAccess>]
type InvoiceStatus =
  | Draft
  | Final
  | Sent
  | Paid

[<CLIMutable>]
type InvoiceListProjectionState =
  { Id: InvoiceId
    Updated: DateTimeOffset
    Status: InvoiceStatus }
// interface IProjectionState

let invoiceListZero =
  { Id = InvoiceId.FromRaw(Guid.Empty)
    Updated = DateTimeOffset.MinValue
    Status = InvoiceStatus.Draft }

let invoiceListEvolve =
  fun (state: InvoiceListProjectionState) (event: InvoiceEventEnvelope) ->
    match event.Payload with
    | Event.DraftCreated _ ->
      { Id = event.StreamId
        Updated = event.Timestamp
        Status = InvoiceStatus.Draft }
    | Event.DraftUpdated _ ->
      { state with
          Updated = event.Timestamp
          Status = InvoiceStatus.Draft }
    | Event.Finalized _ ->
      { state with
          Updated = event.Timestamp
          Status = InvoiceStatus.Final }

let stage (state: InvoiceListProjectionState) = ()
let shouldDelete (state: InvoiceListProjectionState) (event: InvoiceEventEnvelope) = false
