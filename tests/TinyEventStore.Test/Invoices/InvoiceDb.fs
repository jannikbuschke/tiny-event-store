module MyTestDomain.Invoicing.Db

open System
open Core
open Microsoft.EntityFrameworkCore
open TinyEventStore.Ef.DbContext

type Id = InvoiceId

type InvoicingDb =
  inherit DbContext
  new(options: DbContextOptions<InvoicingDb>) = { inherit DbContext(options) }

  override _.OnModelCreating(modelBuilder) =
    modelBuilder.AddMultiEventStore2<Id, Guid, string>(
      (Id.ToRaw, Id.FromRaw),
      "invoice",
      fun config ->
        config.WithStreamType<Event, EventHeader>("invoice") |> ignore
        config.WithStreamType<InvoiceSettingsEvent, EventHeader>("settings") |> ignore
        ()
    )
    |> ignore
