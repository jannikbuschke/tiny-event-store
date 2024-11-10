module TinyEventStore.Test.Chess.Delete

open System
open Chess
open Microsoft.AspNetCore.Http
open Microsoft.EntityFrameworkCore
open Microsoft.Extensions.DependencyInjection
open TinyEventStore.Test.Chess.Db
open Xunit
open Serilog
open FsToolkit.ErrorHandling
open TinyEventStore.Test.Chess

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

let settingsId = GameId.FromRaw 2
let id = GameId.FromRaw 1


[<Fact>]
let ``Delete`` () =
  taskResult {
    use scope0 = serviceProvider.CreateScope()
    let httpContext = DefaultHttpContext(RequestServices = scope0.ServiceProvider)

    let! result0 =
      (id, [ Chess.Event.GameCreated defaultPosition ])
      |> Handler.appendEvents httpContext

    let! result1 = Handler.handleGameCommand httpContext (id, Chess.Command.Delete)

    let store = Handler.store.getDb httpContext.RequestServices

    return ()
  }
  |> TaskResult.mapError (fun x ->
    failwith (sprintf "Task result failed: %s" x)
    ())
