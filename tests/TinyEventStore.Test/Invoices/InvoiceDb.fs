module MyDomain.Invoicing.Db

open System
open Core
open Microsoft.EntityFrameworkCore
open MyDomain.Invoicing.Core
open MyDomain.Invoicing.Projections
open TinyEventStore
open TinyEventStore.Ef
open TinyEventStore.EfPure

type Id = InvoiceId

type InvoicingDb =
  inherit DbContext
  new(options: DbContextOptions<InvoicingDb>) = { inherit DbContext(options) }

  override this.OnModelCreating(modelBuilder) =
    modelBuilder.AddEventStore<Id,Guid,Event,EventHeader>((Id.ToRaw,Id.FromRaw),"Invoice")
    //TODO move
    // modelBuilder.AddEventStore()
    // configureEventStore<Id, Guid, Core.Event, Core.EventHeader>
    //   modelBuilder
    //   (Id.ToRaw, Id.FromRaw)
    //   "Invoice.Streams"
    //   "Invoice.Events"
    ()
