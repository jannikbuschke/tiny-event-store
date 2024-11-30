module TinyEventStore.Test.Chess.Append

open Chess
open Microsoft.EntityFrameworkCore
open TinyEventStore.Test.Chess.Db
open Xunit
open FsToolkit.ErrorHandling
open TinyEventStore.Test.Chess
open TinyEventStore.Test.Context
open TinyEventStore.Check
open System.Linq

let testCases =
  [ esTest<ChessDb> "Appending no events should fail"
    <| fun ctx ->
      taskResult {
        // Arrange
        let id = GameId.FromRaw 1
        let httpContext = ctx.CreateHttpContext()

        try
          let! _ = (id, []) |> Handler.appendEvents httpContext
          Assert.Fail()
        with _ ->
          Assert.True true
      }

    esTest<ChessDb> "Consecutive appends should update projection"
    <| fun ctx ->
      taskResult {
        // Arrange
        let rnd = System.Random 1
        let id = GameId.FromRaw 1
        let httpContext = ctx.CreateHttpContext()
        let! _ = (id, [ Event.GameCreated defaultPosition ]) |> Handler.appendEvents httpContext
        let store = Handler.store.getDb httpContext.RequestServices
        let! item = store.ChessGames.FirstOrDefaultAsync()
        expect <@ box item <> null @>

        // Act
        let httpContext = ctx.CreateHttpContext()
        let store = Handler.store
        let! state, _ = store.rehydrateLatest2 httpContext.RequestServices id

        let pieceMoved =
          TinyEventStore.Test.Data.Streams.moveRandomPiece state.InitalPosition rnd

        let! _ = (id, [ pieceMoved ]) |> Handler.appendEvents httpContext

        // Assert
        let store = Handler.store.getDb httpContext.RequestServices

        let! item0 =
          store.ChessGames
            .Select(fun x -> {| Id = x.Id; Version = x.Version |})
            .FirstOrDefaultAsync()

        expect <@ item0 = {| Version = 2u; Id = id |} @>

        let! item = store.ChessGames.FirstOrDefaultAsync()

        expect
          <@
            item = { ChessGameListItem.Id = id
                     Version = 2u
                     IsFinished = false
                     History = " h1Q" }
          @>
      } ]
