module Program

// let tests = testList "replays" testCases
let x =
  Expecto.Tests.testList "replay" TinyEventStore.Test.Chess.ProjectionReplayExpecto.testCases

let y = Expecto.Tests.testList "replay" TinyEventStore.Test.Chess.Delete.testCases

let all = Expecto.Tests.testList "all" [ x; y ]

[<EntryPoint>]
let main argv =
  Expecto.Tests.runTestsWithArgs Expecto.Tests.defaultConfig argv all
