module TinyEventStore.Test.Chess.Delete

open Chess
open Microsoft.EntityFrameworkCore
open TinyEventStore.Test.Chess.Db
open Xunit
open FsToolkit.ErrorHandling
open TinyEventStore.Test.Chess
open TinyEventStore.Test.Context

let testCases =
  [ esTest<ChessDb> "Delete command should delete read model"
    <| fun ctx ->
      taskResult {
        let httpContext = ctx.CreateHttpContext()
        let id = GameId.FromRaw 1

        let! _ = (id, [ Event.GameCreated defaultPosition ]) |> Handler.appendEvents httpContext

        let store = Handler.store.getDb httpContext.RequestServices
        let! item = store.ChessGames.FirstAsync()
        TinyEventStore.Check.expect <@ item.Id = id @>

        let httpContext = ctx.CreateHttpContext()
        let! _ = Handler.handleGameCommand httpContext (id, Command.Delete)

        let! item = store.ChessGames.FirstOrDefaultAsync()
        Assert.Null item
        return ()
      }
    fesTest<ChessDb> "Append GameFinished should delete read model"
    <| fun ctx ->
      taskResult {
        // Arrange
        let httpContext = ctx.CreateHttpContext()
        let id = GameId.FromRaw 1

        let! _ = (id, [ Event.GameCreated defaultPosition ]) |> Handler.appendEvents httpContext

        let store = Handler.store.getDb httpContext.RequestServices
        let! item = store.ChessGames.FirstAsync()
        TinyEventStore.Check.expect <@ item.Id = id @>

        // Act
        let httpContext = ctx.CreateHttpContext()
        let! _ = (id, [ Event.Deleted ]) |> Handler.appendEvents httpContext

        // Assert
        let! item = store.ChessGames.FirstOrDefaultAsync()
        Assert.Null item
        return ()
      }
    esTest<ChessDb> "Events after deletion event should keep the stream deleted"
    <| fun ctx ->
      taskResult {
        let httpContext = ctx.CreateHttpContext()
        let id = GameId.FromRaw 1

        let! _ =
          (id, [ Event.GameCreated defaultPosition; Event.GameFinished ])
          |> Handler.appendEvents httpContext

        let store = Handler.store.getDb httpContext.RequestServices
        let! item = store.ChessGames.FirstOrDefaultAsync()
        TinyEventStore.Check.expect <@ box item = null @>

        // let httpContext = ctx.CreateHttpContext()
        // let! _ = Handler.handleGameCommand httpContext (id, Command.Delete)
        //
        // let! item = store.ChessGames.FirstOrDefaultAsync()
        // Assert.Null item
        // TinyEventStore.Check.expect <@ box item = null @>
        return ()
      } ]

// let tests = testList "deletes" testCases
