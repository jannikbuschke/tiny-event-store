module Program


let all =
  Expecto.Tests.testList
    "all"
    [ Expecto.Tests.testList "replay" TinyEventStore.Test.Chess.ProjectionReplayExpecto.testCases
      Expecto.Tests.testList "append" TinyEventStore.Test.Chess.Append.testCases
      Expecto.Tests.testList "delete" TinyEventStore.Test.Chess.Delete.testCases ]

[<EntryPoint>]
let main argv =
  Expecto.Tests.runTestsWithArgs Expecto.Tests.defaultConfig argv all
