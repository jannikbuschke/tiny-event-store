[<AutoOpen>]
module TinyEventStore.Check

open Microsoft.FSharp.Quotations.Patterns
open Swensen.Unquote
open Xunit.Sdk

let testWithReason expression reason =
  try
    Assertions.test expression
  with :? TrueException as e ->

    let diff =
      match expression with
      | Call(_, methodInfo, [ a; b ]) when methodInfo.Name = "op_Equality" ->
        let a = a.Eval()
        let b = b.Eval()

        let diffMethod =
          typeof<DEdge.Diffract.Differ>
            .GetMethod(nameof DEdge.Diffract.Differ.Diff)
            .MakeGenericMethod
            [| a.GetType() |]

        let diff: DEdge.Diffract.Diff option =
          downcast diffMethod.Invoke(null, [| b; a; null |])

        let diffString = DEdge.Diffract.Differ.ToString diff
        $"\nDiff = \n{diffString}"
      | _ -> ""

    raise (
      TrueException.ForNonTrueValue(
        reason
        + diff
        + "\n------------------------------------\nUnquote Message:\n"
        + e.Message,
        false
      )
    )

let expect expression = testWithReason expression ""

let assertOk x =
  x |> Result.mapError (fun e -> Xunit.Assert.Fail(e.ToString()))

open FsToolkit.ErrorHandling

let assertTaskResultOk x =
  x |> TaskResult.mapError (fun e -> (Xunit.Assert.Fail(e.ToString())))
