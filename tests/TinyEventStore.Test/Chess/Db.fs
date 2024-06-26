module TinyEventStore.Test.Chess.Db

open System
open System.Collections.Generic
open Microsoft.EntityFrameworkCore
open TinyEventStore
open TinyEventStore.Ef
open TinyEventStore.EfPure
open TinyEventStore.Ef.Storables
open TinyEventStore.Ef.DbContext

type Id = Chess.GameId
type GameEvent = Chess.Event

type ChessSettings = { DefaultGameTime: TimeSpan }

type SettingsCommand =
  | Create of ChessSettings
  | Update of ChessSettings

type SettingsEvent =
  | Created of ChessSettings
  | Updated of ChessSettings

module SettingsLogic =
  let zero = { ChessSettings.DefaultGameTime = TimeSpan.Zero }
  let evolve state event =
    match event with
    | Created settings -> settings
    | Updated settings -> settings
  let handle state command =
    match command with
    | SettingsCommand.Create settings -> [SettingsEvent.Created settings]
    | SettingsCommand.Update settings -> [SettingsEvent.Updated settings]

type ChessEventHeader = Dictionary<string, obj>

type ChessEventEnvelope = EventEnvelope<Id, GameEvent, ChessEventHeader>

type ChessDb =
  inherit DbContext
  new(options: DbContextOptions<ChessDb>) = { inherit DbContext(options) }

  override this.OnModelCreating(modelBuilder) =

    modelBuilder.AddMultiEventStore2<Id, int64>((Id.ToRaw, Id.FromRaw), "chess", (fun model ->
        model.WithStreamType<GameEvent, ChessEventHeader>("chess_game")
        model.WithStreamType<SettingsEvent, ChessEventHeader>("chess_settings")
        // model
        //   .HasValue<StorableStream<Id, GameEvent, ChessEventHeader>>("chess_game")
        //   .HasValue<StorableStream<Id, SettingsEvent, ChessEventHeader>>("chess_settings")
        ())

    )
    |> ignore
    //
    // modelBuilder
    //   .Entity<StorableEvent<Id, GameEvent, ChessEventHeader>>()
    //   .Property(fun x -> x.StreamId)
    //   .HasColumnName("StreamId")
    //
    // modelBuilder
    //   .Entity<EfPure.StorableEvent<Id, SettingsEvent, ChessEventHeader>>()
    //   .Property(fun x -> x.StreamId)
    //   .HasColumnName("StreamId")
    //
    // modelBuilder
    //   .Entity<StorableEvent<Id, GameEvent, ChessEventHeader>>()
    //   .Property(fun x -> x.Data)
    //   .HasColumnName("Data")
    //   .HasConversion(Json.serialize,Json.deserialize)
    //
    // modelBuilder
    //   .Entity<StorableEvent<Id, SettingsEvent, ChessEventHeader>>()
    //   .Property(fun x -> x.Data)
    //   .HasColumnName("Data")
    //   .HasConversion(Json.serialize,Json.deserialize)
    //
    // modelBuilder
    //   .Entity<StorableEvent<Id, GameEvent, ChessEventHeader>>()
    //   .HasOne(fun x -> x.Stream)
    //   .WithMany(fun x -> x.Children :> IEnumerable<StorableEvent<Id, GameEvent, ChessEventHeader>>)
    //   .HasForeignKey(fun x -> x.StreamId :> obj)
    //   .HasConstraintName("k1")
    // |> ignore
    //
    // modelBuilder
    //   .Entity<StorableEvent<Id, SettingsEvent, ChessEventHeader>>()
    //   .HasOne(fun x -> x.Stream)
    //   .WithMany(fun x -> x.Children :> IEnumerable<StorableEvent<Id, SettingsEvent, ChessEventHeader>>)
    //   .HasForeignKey(fun x -> x.StreamId :> obj)
    //   .HasConstraintName("k1")
    // |> ignore

    ()
