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
let ``Projection replay`` () =
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
    store.replay

    // TODO delete list item, then replay projection
    let httpContext = createHttpContext ()
    Handler.replay httpContext
    let! _ = Handler.handleGameCommand httpContext (id, Command.Delete)

    let! item = store.ChessGames.FirstOrDefaultAsync()
    Assert.Null item
    // TinyEventStore.Check.expect <@ box item = null @>
    // Assert
    return ()
  }
  |> TaskResult.mapError (fun x ->
    failwith (sprintf "Task result failed: %s" x)
    ())
