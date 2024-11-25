module TinyEventStore.Test.Chess.ProjectionReplayExpecto

open Microsoft.EntityFrameworkCore
open TinyEventStore.Test.Chess.Db
open Serilog
open FsToolkit.ErrorHandling
open Chess
open Microsoft.AspNetCore.Http
open TinyEventStore.Test.Context


let insertRandomGames (httpContext: HttpContext) (rnd: System.Random) (amount: int) (startId: int) =
  taskResult {
    let manyGames = TinyEventStore.Test.Data.Streams.randomGames rnd amount startId

    let actions =
      manyGames
      |> List.map (fun (id, events) ->
        async { return! (id, events) |> Handler.appendEvents httpContext |> Async.AwaitTask })

    for a in actions do
      let! _ = a |> Async.StartAsTask
      ()
  }

let testCases =
  [ esTest "Replay an initalized game"
    <| fun ctx ->
      taskResult {

        // Arrange

        let httpContext = ctx.CreateHttpContext()
        let id = GameId.FromRaw 1

        let i =
          (id, [ Event.GameCreated defaultPosition ]) |> Handler.appendEvents httpContext

        let store = Handler.store
        let db = store.getDb httpContext.RequestServices
        let! item = db.ChessGames.FirstAsync()
        TinyEventStore.Check.expect <@ item.Id = id @>
        let db = store.getDb httpContext.RequestServices
        db.Remove item |> ignore
        let! _ = db.SaveChangesAsync()

        let! item = db.ChessGames.FirstOrDefaultAsync()
        TinyEventStore.Check.expect <@ box item = null @>

        // Act

        let httpContext = ctx.CreateHttpContext()
        do! Handler.replay httpContext Handler.listProjection

        // Assert

        let httpContext = ctx.CreateHttpContext()
        let db = store.getDb httpContext.RequestServices
        let! item = db.ChessGames.FirstAsync()
        TinyEventStore.Check.expect <@ item.Id = id @>
        return ()
      }

    esTest "Replay many: should restore a deleted read model row (many events)"
    <| fun ctx ->
      taskResult {
        // Arrange
        let rnd = new System.Random 10
        let httpContext = ctx.CreateHttpContext()
        do! insertRandomGames httpContext rnd 100 0

        // // insert one in between that we will delete
        let id = GameId.FromRaw 100
        let! _ = (id, [ Event.GameCreated defaultPosition ]) |> Handler.appendEvents httpContext
        // // insert again many
        do! insertRandomGames httpContext rnd 100 101

        // // delete one row in readmodel
        let store = Handler.store
        let db = store.getDb httpContext.RequestServices
        let! item = db.ChessGames.SingleOrDefaultAsync(fun x -> x.Id = id)
        TinyEventStore.Check.expect <@ item.Id = id @>
        let db = store.getDb httpContext.RequestServices
        db.Remove item |> ignore
        let! _ = db.SaveChangesAsync()

        let! item = db.ChessGames.SingleOrDefaultAsync(fun x -> x.Id = id)
        TinyEventStore.Check.expect <@ box item = null @>

        let! _ = db.ChessGames.ExecuteDeleteAsync()

        // Act

        let httpContext = ctx.CreateHttpContext()
        do! Handler.replay httpContext Handler.listProjection

        // Assert
        let httpContext = ctx.CreateHttpContext()
        let db = store.getDb httpContext.RequestServices
        let! item = db.ChessGames.SingleOrDefaultAsync(fun x -> x.Id = id)

        TinyEventStore.Check.expect <@ item.Id = id @>
        return ()
      }
    esTest "replay long game"
    <| fun ctx ->
      taskResult {

        Log.Logger.Information("Start test")
        // Arrange

        let httpContext = ctx.CreateHttpContext()
        let rnd = System.Random(700)
        let moveAmount = 120
        let data = TinyEventStore.Test.Data.Streams.standardGameWithoutResult rnd moveAmount
        let expectedVersion = moveAmount + 1
        let id = GameId.FromRaw 1

        let! _ = (id, data) |> Handler.appendEvents httpContext

        let store = Handler.store
        let db = store.getDb httpContext.RequestServices
        let! item = db.ChessGames.FirstAsync()

        TinyEventStore.Check.expect <@ item.Id = id @>
        TinyEventStore.Check.expect <@ item.Version = uint expectedVersion @>

        let db = store.getDb httpContext.RequestServices
        db.Remove item |> ignore
        let! _ = db.SaveChangesAsync()

        // verify item is deleted
        let! item = db.ChessGames.FirstOrDefaultAsync()
        TinyEventStore.Check.expect <@ box item = null @>

        // Act

        Log.Logger.Information("Acting")
        // try
        let httpContext = ctx.CreateHttpContext()
        do! Handler.replay httpContext Handler.listProjection
        // with e ->
        //   Log.Error(e, "Error while replaying")
        //   Log.CloseAndFlush()
        //   ()

        Log.Logger.Information("Acting done")
        // Assert

        let httpContext = ctx.CreateHttpContext()
        let db = store.getDb httpContext.RequestServices
        let! item = db.ChessGames.FirstAsync()
        TinyEventStore.Check.expect <@ item.Id = id @>
        TinyEventStore.Check.expect <@ item.Version = uint expectedVersion @>
        return ()
      } ]
