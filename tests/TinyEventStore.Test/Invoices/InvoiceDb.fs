module MyDomain.Invoicing.Db

open System
open Core
open Microsoft.EntityFrameworkCore
open MyDomain.Invoicing.Projections
open TinyEventStore

type Id = InvoiceId

type InvoicingDb =
  inherit DbContext
  new(options: DbContextOptions<InvoicingDb>) = { inherit DbContext(options) }

  override this.OnModelCreating(modelBuilder) =
    //TODO move
    modelBuilder.AddEventStore()
    // configureEventStore<Id, Guid, Core.Event, Core.EventHeader>
    //   modelBuilder
    //   (Id.ToRaw, Id.FromRaw)
    //   "Invoice.Streams"
    //   "Invoice.Events"

