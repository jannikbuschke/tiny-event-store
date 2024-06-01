module MyDomain.Invoicing.Core

open System
open TinyEventStore

[<RequireQualifiedAccess>]
type UserId =
  | UserId of Guid

  static member New() = UserId(Guid.NewGuid())
  static member ToRaw(UserId id) = id
  static member FromRaw(id) = UserId(id)

[<RequireQualifiedAccess>]
type InvoiceId =
  | InvoiceId of Guid

  static member New() = InvoiceId(Guid.NewGuid())
  static member ToRaw(InvoiceId id) = id
  static member FromRaw(id) = InvoiceId(id)


type CustomerId =
  | CustomerId of Guid

  static member New(value: Guid) = CustomerId value

type InvoiceNumber =
  | InvoiceNumber of string

  static member New(value: string) =
    if String.IsNullOrWhiteSpace value then
      failwith "invalid invoice number"
    else
      InvoiceNumber value

  static member ToRaw(InvoiceNumber id) = id
  static member FromRaw id = InvoiceNumber id

type DraftPosition =
  { Description: string option
    Quantity: int option
    Price: decimal option }

type InvoicePosition =
  { Description: string
    Quantity: int
    Price: decimal }

  member this.Total() = 0

type InvoiceDraft =
  { InvoiceNumber: InvoiceNumber option
    CustomerId: CustomerId option
    Positions: DraftPosition list }

// Final invoice
type Invoice =
  { InvoiceNumber: InvoiceNumber
    CustomerId: CustomerId
    CustomerName: string
    Positions: InvoicePosition list }

type Command =
  | CreateDraft of InvoiceDraft
  | UpdateDraft of InvoiceDraft
  | Finalize
  | MarkAsPaid

type Event =
  | DraftCreated of InvoiceDraft
  | DraftUpdated of InvoiceDraft
  | Finalized of Invoice // maybe add rendered html here, or just in the send command?
// | Sent of Invoice*Email
// | MarkedAsPaid of Invoice*DateTimeOffset*string
type EventHeader = { UserId: UserId }
type InvoiceEventEnvelope = EventEnvelope<InvoiceId, Event, EventHeader>

type SideEffectEvent<'t> =
  | Created
  | Trying of attempt: int
  | Success
  | Failed

type Email =
  { To: string
    Subject: string
    Body: string }

type Address =
  { Street: string
    City: string
    Zip: string }


type SideEffect = SendInvoiceEmail of Email * Invoice
