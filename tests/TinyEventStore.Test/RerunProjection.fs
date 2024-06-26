module TinyEventStore.Test.RerunProjection

open System
open Microsoft.AspNetCore.Http
open Microsoft.EntityFrameworkCore
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Time.Testing
open MyTestDomain.Invoicing.Core
open MyTestDomain.Invoicing.Db
open TinyEventStore.Ef.Storables
open Xunit
open Serilog

let services = ServiceCollection()

let testId = DateTimeOffset.Now.ToString("yyyy-MM-dd-HH-mm-ss")
let dbName = "tiny-notes-2024-06-25-00-00-00"

let path = System.IO.Path.GetFullPath(".env.local")
let currentDir = System.IO.Directory.GetCurrentDirectory()
dotenv.net.DotEnv.Load(dotenv.net.DotEnvOptions(envFilePaths = [ ".env.local" ]))
let variables = System.Environment.GetEnvironmentVariables()
let connectionString = System.Environment.GetEnvironmentVariable("ConnectionString")
let connectionString' = connectionString.Replace("{dbName}", dbName)

services.AddDbContext<InvoicingDb> (fun x ->
  x.UseNpgsql(connectionString.Replace("{dbName}", dbName))
  |> ignore)
|> ignore

Serilog.Log.Logger <-
  Serilog
    .LoggerConfiguration()
    .WriteTo.File("logs/log-.log", rollingInterval = RollingInterval.Day)
    .CreateLogger()

// let fakeTime = FakeTimeProvider(startDateTime = DateTimeOffset.UtcNow)
//
// services.AddSingleton<TimeProvider>(fakeTime) |> ignore

services.AddLogging (fun loggingbuilder ->
  loggingbuilder.AddSerilog(Serilog.Log.Logger)
  |> ignore)
|> ignore

let serviceProvider = services.BuildServiceProvider()
// let time = serviceProvider.GetService<TimeProvider>()

serviceProvider
  .GetService<InvoicingDb>()
  .Database.EnsureDeleted()
|> ignore

serviceProvider
  .GetService<InvoicingDb>()
  .Database.EnsureCreated()
|> ignore

open FsToolkit.ErrorHandling

[<Fact>]
let ``append events`` () =
  taskResult {
    use scope0 = serviceProvider.CreateScope()
    let httpContext = DefaultHttpContext(RequestServices = scope0.ServiceProvider)
    let id = InvoiceId.New()

    let header = { EventHeader.UserId = UserId.FromRaw(Guid.Empty) }

    let e1 =
      InvoiceEvent.DraftCreated
        { InvoiceNumber = None
          CustomerId = None
          Positions = [] },
      header

    let e2 =
      InvoiceEvent.DraftUpdated
        { InvoiceNumber = InvoiceNumber.FromRaw("123") |> Some
          CustomerId = None
          Positions = [] },
      header

    let db = httpContext.RequestServices.GetService<InvoicingDb>()
    let! operationResult =
      MyTestDomain.Invoicing.EventStore.store.appendEvents httpContext.RequestServices id [ e1;e2 ]
    MyTestDomain.Invoicing.EventStore.store.updateEventStore2 httpContext.RequestServices operationResult
    let debug = db.ChangeTracker.DebugView.LongView
    let! result = db.SaveChangesAsync()
    do! result > 0 |> Result.requireTrue "Expected some changes should be 2 I guess"

    use scope0 = serviceProvider.CreateScope()
    let httpContext = DefaultHttpContext(RequestServices = scope0.ServiceProvider)
    let! statesAndStreams =  MyTestDomain.Invoicing.EventStore.store.rerunProject scope0.ServiceProvider

    let fo = "x"
    // let! result1 =
    //   MyTestDomain.Invoicing.EventStore.handleCommand
    //     httpContext
    //     (id,
    //      MyTestDomain.Invoicing.Core.Command.CreateDraft
    //        { InvoiceNumber = None
    //          CustomerId = None
    //          Positions = [] })
    //
    // use scope1 = serviceProvider.CreateScope()
    // let httpContext = DefaultHttpContext(RequestServices = scope1.ServiceProvider)
    // do! System.Threading.Tasks.Task.Delay(10)
    //
    // let! result2 =
    //   MyTestDomain.Invoicing.EventStore.handleCommand
    //     httpContext
    //     (id,
    //      MyTestDomain.Invoicing.Core.Command.UpdateDraft
    //        { InvoiceNumber = InvoiceNumber "123" |> Some
    //          CustomerId = None
    //          Positions = [] })
    //
    // use scope1 = serviceProvider.CreateScope()
    //
    // let! state = MyTestDomain.Invoicing.EventStore.store.rehydrateLatest2 scope1.ServiceProvider id
    //
    // let db = scope1.ServiceProvider.GetService<InvoicingDb>()
    //
    // let! streamCount =
    //   db
    //     .Set<StorableStream<InvoiceId, MyTestDomain.Invoicing.Core.Event, MyTestDomain.Invoicing.Core.EventHeader>>()
    //     .CountAsync()
    //
    // Assert.Equal(1, streamCount)
    //
    // let! eventCount =
    //   db
    //     .Set<StorableEvent<InvoiceId, MyTestDomain.Invoicing.Core.Event, MyTestDomain.Invoicing.Core.EventHeader>>()
    //     .CountAsync()
    //
    // Assert.Equal(2, eventCount)
    //
    // Assert.True true
    // let deleteDb = false
    //
    // if deleteDb = true then
    //   serviceProvider.GetService<InvoicingDb>().Database.EnsureDeleted() |> ignore

    return ()
  }
  |> Task.map (fun x ->
    match x with
    | Result.Error e -> failwith e
    | Result.Ok _ -> ())
