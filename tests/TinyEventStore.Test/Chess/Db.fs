module TinyEventStore.Test.Chess.Db

open System
open System.Collections.Generic
open Microsoft.EntityFrameworkCore
open TinyEventStore
open TinyEventStore.Ef.DbContext

type Id = Chess.GameId
type GameEvent = Chess.Event

type ChessSettings = { DefaultGameTime: TimeSpan }

[<RequireQualifiedAccess>]
type SettingsCommand =
  | Create of ChessSettings
  | Update of ChessSettings

[<RequireQualifiedAccess>]
type SettingsEvent =
  | Created of ChessSettings
  | Updated of ChessSettings

module SettingsLogic =
  let zero = { ChessSettings.DefaultGameTime = TimeSpan.Zero }

  let evolve _ event =
    match event with
    | SettingsEvent.Created settings -> settings
    | SettingsEvent.Updated settings -> settings

  let handle _ command =
    match command with
    | SettingsCommand.Create settings -> [ SettingsEvent.Created settings ]
    | SettingsCommand.Update settings -> [ SettingsEvent.Updated settings ]

type ChessEventHeader = Dictionary<string, obj>

type ChessEventEnvelope = EventEnvelope<Id, GameEvent, ChessEventHeader>

[<CLIMutable>]
type ChessGameListItem =
  { Id: Id
    Version: uint
    IsFinished: bool
    History: string }

type ChessDb =
  inherit DbContext
  new(options: DbContextOptions<ChessDb>) = { inherit DbContext(options) }

  [<DefaultValue>]
  val mutable private chessGames: DbSet<ChessGameListItem>

  member this.ChessGames
    with get () = this.chessGames
    and set v = this.chessGames <- v

  override this.OnModelCreating(modelBuilder) =
    modelBuilder.Entity<ChessGameListItem>(fun e ->
      e.Property(fun x -> x.Id).HasConversion(Id.ToRaw, Id.FromRaw) |> ignore

      ())
    |> ignore

    modelBuilder.AddMultiEventStore2<Id, int64, string>(
      (Id.ToRaw, Id.FromRaw),
      "chess",
      (fun model ->
        model.WithStreamType<GameEvent, ChessEventHeader>("chess_game") |> ignore

        model.WithStreamType<SettingsEvent, ChessEventHeader>("chess_settings")
        |> ignore

        ())

    )
    |> ignore

    ()
