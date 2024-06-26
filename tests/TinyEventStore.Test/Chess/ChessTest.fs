module TinyEventStore.Test.Chess.ChessTest

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
let ``do some and save changes`` () =
  task {
    use scope0 = serviceProvider.CreateScope()
    let httpContext = DefaultHttpContext(RequestServices = scope0.ServiceProvider)

    let settingsId = GameId.FromRaw 2

    let! result1 =
      TinyEventStore.Test.Chess.Handler.handleSettingsCommand httpContext (settingsId, SettingsCommand.Create { DefaultGameTime = TimeSpan.FromMinutes 5 })

    let id = GameId.FromRaw 1

    let! result1 = TinyEventStore.Test.Chess.Handler.handleCommand httpContext (id, Chess.Command.CreateGame)

    use scope1 = serviceProvider.CreateScope()
    let httpContext = DefaultHttpContext(RequestServices = scope1.ServiceProvider)
    do! System.Threading.Tasks.Task.Delay(10)

    let! result2 =
      TinyEventStore.Test.Chess.Handler.handleCommand
        httpContext
        (id,
         Chess.Command.MovePiece
           { From = Square.FromRaw "e2"
             To = Square.FromRaw "e4"
             ChessPiece = (Piece.Bishop, Color.White) })

    use scope1 = serviceProvider.CreateScope()

    let! state = TinyEventStore.Test.Chess.Handler.store.rehydrateLatest2 scope1.ServiceProvider id

    return ()
  }


[<Fact>]
let ``Many events on one stream`` () =
  task {
    use scope0 = serviceProvider.CreateScope()
    let httpContext = DefaultHttpContext(RequestServices = scope0.ServiceProvider)
    let id = GameId.FromRaw 1
    let! result1 = TinyEventStore.Test.Chess.Handler.handleCommand httpContext (id, Chess.Command.CreateGame)

    let move () =
      task {
        use scope0 = serviceProvider.CreateScope()
        let httpContext = DefaultHttpContext(RequestServices = scope0.ServiceProvider)

        let! result2 =
          TinyEventStore.Test.Chess.Handler.handleCommand
            httpContext
            (id,
             Chess.Command.MovePiece
               { From = Square.FromRaw "e2"
                 To = Square.FromRaw "e4"
                 ChessPiece = (Piece.Bishop, Color.White) })

        return ()
      }
    let overallTime = new Stopwatch()
    overallTime.Start()

    let durations = ResizeArray()
    for i in 1..1000 do
      let stopWatch = new Stopwatch()
      stopWatch.Reset()
      stopWatch.Start()
      do! move ()
      // do! Threading.Tasks.Task.Delay(500)
      durations.Add(stopWatch.Elapsed)
      let elapsed = stopWatch.Elapsed
      let elapsedTicks = stopWatch.ElapsedTicks
      let elapsedMs = stopWatch.ElapsedMilliseconds
      stopWatch.Stop()
      ()

    overallTime.Stop()
    let overallElapsed = overallTime.Elapsed
    use scope1 = serviceProvider.CreateScope()
    let httpContext = DefaultHttpContext(RequestServices = scope1.ServiceProvider)
    let! state = TinyEventStore.Test.Chess.Handler.store.rehydrateLatest scope1.ServiceProvider id

    return ()
  }
