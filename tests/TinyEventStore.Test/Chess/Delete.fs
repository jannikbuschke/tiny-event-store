module TinyEventStore.Test.Chess.Delete

open Chess
open Microsoft.EntityFrameworkCore
open TinyEventStore.Test.Chess.Db
open Xunit
open FsToolkit.ErrorHandling
open TinyEventStore.Test.Chess
open TinyEventStore.Test.Context
open TinyEventStore.Check

let testCases =
  [ esTest<ChessDb> "Delete command should delete read model"
    <| fun ctx ->
      taskResult {
        // Arrange
        let httpContext = ctx.CreateHttpContext()
        let id = GameId.FromRaw 1

        let! _ = (id, [ Event.GameCreated defaultPosition ]) |> Handler.appendEvents httpContext

        // Act
        let! _ = Handler.handleGameCommand (ctx.CreateHttpContext()) (id, Command.Delete)

        // Assert
        let store = Handler.store.getDb (ctx.CreateHttpContext()).RequestServices
        let! item = store.ChessGames.FirstOrDefaultAsync()
        Assert.Null item
        return ()
      }
    esTest<ChessDb> "Append Deleted should delete read model"
    <| fun ctx ->
      taskResult {
        // Arrange
        let id = GameId.FromRaw 1

        let! _ =
          (id, [ Event.GameCreated defaultPosition ])
          |> Handler.appendEvents (ctx.CreateHttpContext())

        // Act
        let! _ = (id, [ Event.Deleted ]) |> Handler.appendEvents (ctx.CreateHttpContext())

        // Assert
        let store = Handler.store.getDb (ctx.CreateHttpContext().RequestServices)
        let! item = store.ChessGames.FirstOrDefaultAsync()
        Assert.Null item
        return ()
      }
    esTest<ChessDb> "Delete command should mark stream as deleted"
    <| fun ctx ->
      taskResult {
        // Arrange
        let id = GameId.FromRaw 1

        let! _ =
          (id, [ Event.GameCreated defaultPosition ])
          |> Handler.appendEvents (ctx.CreateHttpContext())

        // Act
        let! _ = Handler.handleGameCommand (ctx.CreateHttpContext()) (id, Command.Delete)

        // Assert
        let! _, stream = Handler.store.rehydrateLatest2 (ctx.CreateHttpContext()).RequestServices id
        expect <@ stream.IsDeleted = true @>

        return ()
      }
    esTest<ChessDb> "Delete event should mark stream as deleted"
    <| fun ctx ->
      taskResult {
        // Arrange
        let id = GameId.FromRaw 1

        // Act
        let! _ =
          (id, [ Event.GameCreated defaultPosition; Event.Deleted ])
          |> Handler.appendEvents (ctx.CreateHttpContext())

        // Assert
        let httpContext = ctx.CreateHttpContext()
        let store = Handler.store
        let! _, stream = store.rehydrateLatest2 httpContext.RequestServices id
        expect <@ stream.IsDeleted = true @>

        return ()
      }
    esTest<ChessDb> "Events after deletion event should keep the stream deleted"
    <| fun ctx ->
      taskResult {
        // Arrange
        let rnd = System.Random 5
        let id = GameId.FromRaw 1

        let httpContext = ctx.CreateHttpContext()

        let! _ =
          (id, [ Event.GameCreated defaultPosition; Event.Deleted ])
          |> Handler.appendEvents httpContext

        // Act
        let httpContext = ctx.CreateHttpContext()
        let store = Handler.store
        let! state, _ = store.rehydrateLatest2 httpContext.RequestServices id

        let pieceMoved =
          TinyEventStore.Test.Data.Streams.moveRandomPiece state.InitalPosition rnd

        let! _ = (id, [ pieceMoved ]) |> Handler.appendEvents httpContext

        // Assert
        let store = Handler.store.getDb httpContext.RequestServices
        let! item = store.ChessGames.FirstOrDefaultAsync()
        expect <@ box item = null @>
        return ()
      } ]
