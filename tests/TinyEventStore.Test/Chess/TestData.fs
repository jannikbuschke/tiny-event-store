module TinyEventStore.Test.Data

open Chess

module Streams =
  let defaultGameCreatedEvent = Event.GameCreated defaultPosition
  let gameInitialized = [ defaultGameCreatedEvent ]

  let randomFreeSquare (position: Position) (rnd: System.Random) =
    let freeSquares = getFreeSquares position
    let rnd = rnd.Next freeSquares.Length
    freeSquares.Item rnd
  // if position.Length = 64 then
  //   failwith "The given position does not have free squares"
  //
  // let mutable square = None
  //
  // while square = None do
  //   let randomSquare = randomSquare rnd
  //
  //   if not (position |> List.exists (fun (s, _) -> s = randomSquare)) then
  //     square <- Some randomSquare
  //
  // square

  let moveRandomPiece (position: Position) (rnd: System.Random) =
    let (square, piece) = position.Item(rnd.Next(position.Length))
    let target = randomFreeSquare position rnd

    Event.PieceMoved(
      { PieceMovement.From = square
        To = target
        ChessPiece = piece }
    )

  let randomMovements (position: Position) (rnd: System.Random) (quantity: int) =
    let state = position, []

    let result =
      [ 0 .. (quantity - 1) ]
      |> List.fold
        (fun (position, movements) _ ->
          let move = moveRandomPiece position rnd
          (position, movements @ [ move ]))
        state
      |> snd

    result

  let standardGameWithoutResult (rnd: System.Random) (length: int) =
    let startPosition = defaultPosition
    let start = Event.GameCreated startPosition
    let result = randomMovements startPosition rnd length
    start :: result

  let initianizedStandardGame () = [ defaultGameCreatedEvent ]
  let longStandardGame (rnd: System.Random) = standardGameWithoutResult rnd 120
  let mediumStandardGame (rnd: System.Random) = standardGameWithoutResult rnd 50
  let shortStandardGame (rnd: System.Random) = standardGameWithoutResult rnd 25
  let veryShortStandardGame (rnd: System.Random) = standardGameWithoutResult rnd 5

  let randomGames (rnd: System.Random) (quantity: int) (startId: int) =
    [ 0 .. (quantity - 1) ]
    |> List.map (fun _ ->
      match rnd.Next(4) with
      | 0 -> veryShortStandardGame
      | 1 -> shortStandardGame
      | 2 -> mediumStandardGame
      | 3 -> longStandardGame
      | _ -> failwith "unexpected ranrom value")
    |> List.mapi (fun i gen -> GameId.FromRaw((int64) (startId + i)), gen rnd)
