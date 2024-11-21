module TinyEventStore.Test.Context

open Expecto
open Expecto.Flip
open Microsoft.Extensions.DependencyInjection
open Microsoft.AspNetCore.Http

open Microsoft.EntityFrameworkCore
open Serilog

type TestContext =
  { Services: ServiceProvider
    CreateHttpContext: unit -> HttpContext
    Teardown: unit -> unit }

let bootstrapTestContext<'db when 'db :> DbContext> (dbName: string) =
  let dbName = dbName.Replace(" ", "-")
  let services = ServiceCollection()
  // let testId = DateTimeOffset.Now.ToString("yyyy-MM-dd-HH-mm-ss")
  let dbName = $"test-tiny-event-store-chess-{dbName}"

  // let path = System.IO.Path.GetFullPath(".env.local")
  // let currentDir = System.IO.Directory.GetCurrentDirectory()
  dotenv.net.DotEnv.Load(dotenv.net.DotEnvOptions(envFilePaths = [ ".env.local" ]))
  // let variables = System.Environment.GetEnvironmentVariables()
  let connectionString = System.Environment.GetEnvironmentVariable("ConnectionString")

  services.AddDbContext<'db>(fun x -> x.UseNpgsql(connectionString.Replace("{dbName}", dbName)) |> ignore)
  |> ignore

  Log.Logger <-
    Serilog
      .LoggerConfiguration()
      .WriteTo.File("logs/log-.log", rollingInterval = RollingInterval.Day)
      .WriteTo.Seq("http://localhost:5341")
      .CreateLogger()

  services.AddLogging(fun loggingbuilder -> loggingbuilder.AddSerilog(Serilog.Log.Logger) |> ignore)
  |> ignore

  let serviceProvider = services.BuildServiceProvider()

  serviceProvider.GetService<'db>().Database.EnsureDeleted() |> ignore

  serviceProvider.GetService<'db>().Database.EnsureCreated() |> ignore

  { Services = serviceProvider
    CreateHttpContext =
      fun () ->
        let scope0 = serviceProvider.CreateScope()
        DefaultHttpContext(RequestServices = scope0.ServiceProvider)
    Teardown = fun () -> serviceProvider.GetService<'db>().Database.EnsureDeleted() |> ignore }



let esTest name f =
  testTask name {
    let ctx = bootstrapTestContext name

    let! result = f ctx
    result |> Result.iter ctx.Teardown
    result |> Expect.isOk "Expected Ok"
  }
