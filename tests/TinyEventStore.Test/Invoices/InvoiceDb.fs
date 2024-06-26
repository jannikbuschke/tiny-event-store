module MyTestDomain.Invoicing.Db

open System
open System.Collections.Generic
open Core
open Microsoft.EntityFrameworkCore
open TinyEventStore
// open TinyEventStore.Ef
open TinyEventStore.Ef.Storables
open TinyEventStore.Ef.DbContext

type Id = InvoiceId

type InvoicingDb =
  inherit DbContext
  new(options: DbContextOptions<InvoicingDb>) = { inherit DbContext(options) }

  override this.OnModelCreating(modelBuilder) =
    modelBuilder.AddMultiEventStore2<Id, Guid>((Id.ToRaw, Id.FromRaw), "invoice", fun config ->
      config.WithStreamType<Event, EventHeader>("invoice")
      config.WithStreamType<InvoiceSettingsEvent, EventHeader>("settings")
      config.Build()
      ()
    )
    //
    // modelBuilder.AddMultiEventStore<Id,Guid>(
    //   (Id.ToRaw,Id.FromRaw),
    //   (fun model ->
    //     model
    //       .HasValue<StorableStream<Id, Event, EventHeader>>("invoice")
    //       .HasValue<StorableStream<Id, InvoiceSettingsEvent, EventHeader>>("settings")
    //
    //     ()
    //   ),
    //   (fun model ->
    //     model
    //         .HasValue<StorableEvent<Id, Event, EventHeader>>("invoice")
    //         .HasValue<StorableEvent<Id, InvoiceSettingsEvent, EventHeader>>("settings")
    //     ()
    //     ),
    //   (fun model->())
    // ) |> ignore
    //
    // modelBuilder
    //   .Entity<StorableEvent<Id, Event, EventHeader>>()
    //   .Property(fun x -> x.StreamId)
    //   .HasColumnName("StreamId")
    // modelBuilder
    //   .Entity<StorableEvent<Id, InvoiceSettingsEvent, EventHeader>>()
    //   .Property(fun x -> x.StreamId)
    //   .HasColumnName("StreamId")
    //
    // modelBuilder
    //   .Entity<StorableEvent<Id, InvoiceEvent, EventHeader>>()
    //   .Property(fun x -> x.Data)
    //   .HasColumnName("Data")
    // modelBuilder
    //   .Entity<StorableEvent<Id, InvoiceSettingsEvent, EventHeader>>()
    //   .Property(fun x -> x.Data)
    //   .HasColumnName("Data")
    //
    // modelBuilder
    //   .Entity<StorableEvent<Id, Event, EventHeader>>()
    //   .HasOne(fun x -> x.Stream)
    //   .WithMany(fun x -> x.Children :> IEnumerable<StorableEvent<Id, Event, EventHeader>>)
    //   .HasForeignKey(fun x -> x.StreamId :> obj)
    //   .HasConstraintName("k1")
    // |> ignore
    //
    // modelBuilder
    //   .Entity<StorableEvent<Id, InvoiceSettingsEvent, EventHeader>>()
    //   .HasOne(fun x -> x.Stream)
    //   .WithMany(fun x -> x.Children :> IEnumerable<StorableEvent<Id, InvoiceSettingsEvent, EventHeader>>)
    //   .HasForeignKey(fun x -> x.StreamId :> obj)
    //   .HasConstraintName("k1")
    // |> ignore

    // modelBuilder.AddEventStore<Id,Guid,Event,EventHeader>((Id.ToRaw,Id.FromRaw),"Invoice")
    //TODO move
    // modelBuilder.AddEventStore()
    // configureEventStore<Id, Guid, Core.Event, Core.EventHeader>
    //   modelBuilder
    //   (Id.ToRaw, Id.FromRaw)
    //   "Invoice.Streams"
    //   "Invoice.Events"
    ()
