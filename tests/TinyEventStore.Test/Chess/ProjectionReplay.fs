module TinyEventStore.Test.Chess.ProjectionReplay

open Microsoft.EntityFrameworkCore
open Microsoft.Extensions.DependencyInjection
open TinyEventStore.Test.Chess.Db
open Xunit
open Serilog
open FsToolkit.ErrorHandling
open Microsoft.AspNetCore.Http
open Chess

let bootstrapTestDatabase () =
  let services = ServiceCollection()
  // let testId = DateTimeOffset.Now.ToString("yyyy-MM-dd-HH-mm-ss")
  let dbName = "test-tiny-event-store-chess"

  // let path = System.IO.Path.GetFullPath(".env.local")
  // let currentDir = System.IO.Directory.GetCurrentDirectory()
  dotenv.net.DotEnv.Load(dotenv.net.DotEnvOptions(envFilePaths = [ ".env.local" ]))
  // let variables = System.Environment.GetEnvironmentVariables()
  let connectionString = System.Environment.GetEnvironmentVariable("ConnectionString")

  services.AddDbContext<ChessDb>(fun x -> x.UseNpgsql(connectionString.Replace("{dbName}", dbName)) |> ignore)
  |> ignore

  Log.Logger <-
    Serilog
      .LoggerConfiguration()
      .WriteTo.File("logs/log-.log", rollingInterval = RollingInterval.Day)
      .WriteTo.Seq("localhost:5341")
      .CreateLogger()

  services.AddLogging(fun loggingbuilder -> loggingbuilder.AddSerilog(Serilog.Log.Logger) |> ignore)
  |> ignore

  let serviceProvider = services.BuildServiceProvider()

  serviceProvider.GetService<ChessDb>().Database.EnsureDeleted() |> ignore

  serviceProvider.GetService<ChessDb>().Database.EnsureCreated() |> ignore

  fun () ->
    let scope0 = serviceProvider.CreateScope()
    DefaultHttpContext(RequestServices = scope0.ServiceProvider)

let createHttpContext = bootstrapTestDatabase ()

[<Fact>]
let ``Replay one long game game`` () =
  taskResult {
    Log.Logger.Information("Start test")
    // Arrange

    let httpContext = createHttpContext ()
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
    let httpContext = createHttpContext ()
    do! Handler.replay httpContext Handler.listProjection
    // with e ->
    //   Log.Error(e, "Error while replaying")
    //   Log.CloseAndFlush()
    //   ()

    Log.Logger.Information("Acting done")
    // Assert

    let httpContext = createHttpContext ()
    let db = store.getDb httpContext.RequestServices
    let! item = db.ChessGames.FirstAsync()
    TinyEventStore.Check.expect <@ item.Id = id @>
    TinyEventStore.Check.expect <@ item.Version = uint expectedVersion @>
    return ()
  }
  |> TaskResult.mapError (fun x ->
    failwith (sprintf "Task result failed: %s" x)
    ())

[<Fact>]
let ``Replay an initalized game`` () =
  taskResult {

    // Arrange

    let httpContext = createHttpContext ()
    let id = GameId.FromRaw 1

    let! _ = (id, [ GameCreated defaultPosition ]) |> Handler.appendEvents httpContext
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

    let httpContext = createHttpContext ()
    do! Handler.replay httpContext Handler.listProjection

    // Assert

    let httpContext = createHttpContext ()
    let db = store.getDb httpContext.RequestServices
    let! item = db.ChessGames.FirstAsync()
    TinyEventStore.Check.expect <@ item.Id = id @>
    return ()
  }
  |> TaskResult.mapError (fun x ->
    failwith (sprintf "Task result failed: %s" x)
    ())

let insertRandomGames (httpContext: HttpContext) (rnd: System.Random) (amount: int) (startId: int) =
  taskResult {
    let manyGames = TinyEventStore.Test.Data.Streams.randomGames rnd amount startId
    // Arrange

    let actions =
      manyGames
      |> List.map (fun (id, events) ->
        async { return! (id, events) |> Handler.appendEvents httpContext |> Async.AwaitTask })

    for a in actions do
      let! r = a |> Async.StartAsTask
      ()
  }

[<Trait("x", "x")>]
[<Fact>]
let ``Replay many: should restore a deleted read model row (many events)`` () =
  taskResult {
    // Arrange
    let rnd = new System.Random 10
    let httpContext = createHttpContext ()
    do! insertRandomGames httpContext rnd 100 0

    // // insert one in between that we will delete
    let id = GameId.FromRaw 100
    let! _ = (id, [ GameCreated defaultPosition ]) |> Handler.appendEvents httpContext
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

    let httpContext = createHttpContext ()
    do! Handler.replay httpContext Handler.listProjection

    // Assert
    let httpContext = createHttpContext ()
    let db = store.getDb httpContext.RequestServices
    let! item = db.ChessGames.SingleOrDefaultAsync(fun x -> x.Id = id)

    TinyEventStore.Check.expect <@ item.Id = id @>
    return ()
  }
  |> TaskResult.mapError (fun x ->
    failwith (sprintf "Task result failed: %s" x)
    ())
