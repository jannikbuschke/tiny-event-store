module TinyEventStore.Test.Chess.ProjectionReplay

open System
open System.Diagnostics
open Chess
open Microsoft.AspNetCore.Http
open Microsoft.EntityFrameworkCore
open Microsoft.Extensions.DependencyInjection
open TinyEventStore.Test.Chess.Db
open Xunit
open Serilog

let services = ServiceCollection()
let testId = DateTimeOffset.Now.ToString("yyyy-MM-dd-HH-mm-ss")
let dbName = "test-tiny-event-store-chess"

let path = System.IO.Path.GetFullPath(".env.local")
let currentDir = System.IO.Directory.GetCurrentDirectory()
dotenv.net.DotEnv.Load(dotenv.net.DotEnvOptions(envFilePaths = [ ".env.local" ]))
let variables = System.Environment.GetEnvironmentVariables()
let connectionString = System.Environment.GetEnvironmentVariable("ConnectionString")
let connectionString' = connectionString.Replace("{dbName}", dbName)

services.AddDbContext<ChessDb>(fun x -> x.UseNpgsql(connectionString.Replace("{dbName}", dbName)) |> ignore)
|> ignore

//configure Serilog logger that writes to a file
Serilog.Log.Logger <-
  Serilog
    .LoggerConfiguration()
    .WriteTo.File("logs/log-.log", rollingInterval = RollingInterval.Day)
    .CreateLogger()

services.AddLogging(fun loggingbuilder -> loggingbuilder.AddSerilog(Serilog.Log.Logger) |> ignore)
|> ignore

let serviceProvider = services.BuildServiceProvider()

serviceProvider.GetService<ChessDb>().Database.EnsureDeleted() |> ignore

serviceProvider.GetService<ChessDb>().Database.EnsureCreated() |> ignore

[<Fact>]
let ``Projection replay`` () =
  taskResult {
    let httpContext = createHttpContext ()
    let id = GameId.FromRaw 1

    let! _ = (id, [ GameCreated defaultPosition ]) |> Handler.appendEvents httpContext

    let store = Handler.store.getDb httpContext.RequestServices
    let! item = store.ChessGames.FirstAsync()
    TinyEventStore.Check.expect <@ item.Id = id @>
    let db = store.GetDb(httpContext.RequestServices)
    db.Remove(item) |> ignore
    let! _ = db.SaveChangesAsync()
    // TODO delete list item, then replay projection

    let! item = store.ChessGames.FirstOrAsync()
    TinyEventStore.Check.expect <@ item = null @>

    //

    let httpContext = createHttpContext ()
    Handler.replay httpContext
    let! _ = Handler.handleGameCommand httpContext (id, Command.Delete)

    let! item = store.ChessGames.FirstOrDefaultAsync()
    Assert.Null item
    // TinyEventStore.Check.expect <@ box item = null @>
    return ()
  }
  |> TaskResult.mapError (fun x ->
    failwith (sprintf "Task result failed: %s" x)
    ())
