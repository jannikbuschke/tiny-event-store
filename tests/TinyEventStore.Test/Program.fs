module Program


let all =
  Expecto.Tests.testList
    "all"
    [ Expecto.Tests.testList "replay" TinyEventStore.Test.Chess.ProjectionReplayExpecto.testCases
      Expecto.Tests.testList "replay" TinyEventStore.Test.Chess.Delete.testCases ]

[<EntryPoint>]
let main argv =
  Expecto.Tests.runTestsWithArgs Expecto.Tests.defaultConfig argv all
