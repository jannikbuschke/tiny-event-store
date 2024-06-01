module TinyEventStore.Test.Invoices.Test

open System
open Microsoft.AspNetCore.Http
open Microsoft.EntityFrameworkCore
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Time.Testing
open MyDomain.Invoicing.Core
open MyDomain.Invoicing.Db
open MyDomain.Invoicing.Projections
open TinyEventStore
open Xunit
open Serilog

let services = ServiceCollection()

let testId = DateTimeOffset.Now.ToString("yyyy-MM-dd-HH-mm-ss")
let dbName = "my-domain-test-2024-04-19-09-05-31" // $"my-domain-test-{testId}"

dotenv.net.DotEnv.Load(dotenv.net.DotEnvOptions(envFilePaths = [ ".env.local" ]))
let connectionString = System.Environment.GetEnvironmentVariable("ConnectionString")

services.AddDbContext<InvoicingDb>(fun x ->
  x.UseNpgsql(connectionString.Replace("{DatabaseName}", dbName))
  |> ignore
)
|> ignore

//configure Serilog logger that writes to a file
Serilog.Log.Logger <-
  Serilog
    .LoggerConfiguration()
    .WriteTo.File("logs/log-.log", rollingInterval = RollingInterval.Day)
    .CreateLogger()

let fakeTime = FakeTimeProvider(startDateTime = DateTimeOffset.UtcNow)

services.AddSingleton<TimeProvider>(fakeTime) |> ignore

services.AddLogging(fun loggingbuilder -> loggingbuilder.AddSerilog(Serilog.Log.Logger) |> ignore)
|> ignore

let serviceProvider = services.BuildServiceProvider()
let time = serviceProvider.GetService<TimeProvider>()

serviceProvider.GetService<InvoicingDb>().Database.EnsureDeleted() |> ignore

serviceProvider.GetService<InvoicingDb>().Database.EnsureCreated() |> ignore

[<Fact>]
let ``foo`` () =
  task {
    use scope0 = serviceProvider.CreateScope()
    let httpContext = DefaultHttpContext(RequestServices = scope0.ServiceProvider)
    let id = InvoiceId.New()

    let! result1 =
      MyDomain.Invoicing.EventStore.handleCommand
        httpContext
        (id,
         MyDomain.Invoicing.Core.Command.CreateDraft
           { InvoiceNumber = None
             CustomerId = None
             Positions = [] })

    use scope1 = serviceProvider.CreateScope()
    let httpContext = DefaultHttpContext(RequestServices = scope1.ServiceProvider)
    do! System.Threading.Tasks.Task.Delay(10)

    let! result2 =
      MyDomain.Invoicing.EventStore.handleCommand
        httpContext
        (id,
         MyDomain.Invoicing.Core.Command.UpdateDraft
           { InvoiceNumber = InvoiceNumber "123" |> Some
             CustomerId = None
             Positions = [] })

    use scope1 = serviceProvider.CreateScope()
    let db = scope1.ServiceProvider.GetService<InvoicingDb>()

    let! streamCount =
      db
        .Set<Stream<InvoiceId, MyDomain.Invoicing.Core.Event, MyDomain.Invoicing.Core.EventHeader>>()
        .CountAsync()

    Assert.Equal(1, streamCount)

    let! eventCount =
      db
        .Set<EventEnvelope<InvoiceId, MyDomain.Invoicing.Core.Event, MyDomain.Invoicing.Core.EventHeader>>()
        .CountAsync()

    Assert.Equal(2, eventCount)
    let invoice = db.Set<DefaultProjection<Id, InvoiceData>>().Find(id)

    Assert.True(
      invoice.Value =
        InvoiceData.Draft
          { InvoiceNumber = InvoiceNumber "123" |> Some
            CustomerId = None
            Positions = [] }
    )

    Assert.True true
    let deleteDb = false

    if deleteDb = true then
      serviceProvider.GetService<InvoicingDb>().Database.EnsureDeleted() |> ignore

    return ()
  }
