module Program


let all =
  Expecto.Tests.testList
    "all"
    [
      // Expecto.Tests.testList "replay" TinyEventStore.Test.Chess.ProjectionReplayExpecto.testCases
      //   Expecto.Tests.testList "append" TinyEventStore.Test.Chess.Append.testCases
      //   Expecto.Tests.testList "delete" TinyEventStore.Test.Chess.Delete.testCases
      // Expecto.Tests.testList "delete" Theater.Tests.tests
      Expecto.Tests.testList "projections" Theater.ProjectionTests.tests
    ]

[<EntryPoint>]
let main argv =
  // Expecto.Tests.runTestsWithArgs Expecto.Tests.defaultConfig argv all
  Expecto.Tests.runTestsWithCLIArgs [] argv all
