module TinyEventStore.Test.JsonTests

open TinyEventStore
open System
open VerifyTests
open VerifyXunit
open Xunit
open System.Reflection
open System.Text.Json
open System.Text.Json.Serialization
open Serilog

let Match<'a> t = t = typeof<'a>

let MatchGeneric<'a> (t: Type) =
  t.IsGenericType && (t.GetGenericTypeDefinition() = typedefof<'a>)

// list of explicit creators for types that cannot be created automatically
let customCreators: Creator.CustomCreator list = []
// Match<StartAndEndRange<Workday>>, fun _ _ -> { StartAndEndRange.Start = "01.01.2021" |> Workday.parse ; End = "31.01.2021" |> Workday.parse } :> obj
// Match<Calitime.Zeit.Ranges.IRange<Calitime.Zeit.Workday>>, fun _ _ -> Calitime.Zeit.Ranges.NoEndRange(Calitime.Zeit.Workday(2021, 1, 7)) :> obj
// Match<Workday>, fun _ _ -> "01.03.2021" |> Workday.parse :> obj
// Match<Calitime.Zeit.Workday>, fun _ _ -> Calitime.Zeit.Workday(2021, 10, 13) :> obj
// Match<IExpression<EmployeeGuid>>, fun _ _ -> DataExpression(SingleEmployeePipe("E" |> Guid.generate)) :> obj
// Match<IExpression<KontoGuid>>, fun _ _ -> DataExpression(SingleKontoPipe("C" |> Guid.generate)) :> obj
// Match<IExpression<ZeitartGuid>>, fun _ _ -> DataExpression(SingleZeitartenPipe("B" |> Guid.generate)) :> obj
// MatchGeneric<Maybe<_>>, createMaybe
// Match<Calitime.Zeit.Date>, fun _ _ -> Calitime.Zeit.Date(2021, 10, 13) :> obj
// Match<Calitime.Zeit.UtcDateTime<Calitime.Zeit.SimpleDateTime>>, fun _ _ -> Calitime.Zeit.UtcDateTime(Calitime.Zeit.SimpleDateTime(2021, 10, 13, 8, 0)) :> obj
// Match<Calitime.Zeit.ZoneIdName>, fun _ _ -> Calitime.Zeit.ZoneIdName("Europe/Zurich") :> obj
// Match<YearlyRecurrence>, fun _ _ -> YearlyRecurrence(1, 1) :> obj
// Match<ITimeline>, fun _ _ -> Timeline([ ValueInRange("value", Calitime.Zeit.Ranges.EternityRange<Calitime.Zeit.Workday>(), ProjectionState.Final) ]) :> obj
// Match<OperationResult>, fun _ _ -> OperationResult.CreateSuccessful() :> obj
// Match<Type>, fun _ _ -> typeof<int> :> obj
// ]

let storageEncoding =
  JsonUnionEncoding.AdjacentTag
  ||| JsonUnionEncoding.NamedFields
  // ||| JsonUnionEncoding.UnwrapOption
  // ||| JsonUnionEncoding.UnwrapSingleCaseUnions
  ||| JsonUnionEncoding.AllowUnorderedTag

let storageOptions = JsonFSharpOptions(storageEncoding)

let x =
  JsonFSharpOptions()
    .WithUnionAdjacentTag()
    .WithUnwrapOption(false)
    .WithUnionUnwrapSingleCaseUnions()
    .WithUnionAllowUnorderedTag()

let options =
  [ "default", JsonFSharpOptions.Default()
    "newtonsoft-like", JsonFSharpOptions.NewtonsoftLike()
    "toth-like", JsonFSharpOptions.ThothLike()
    "fsharplu-like", JsonFSharpOptions.FSharpLuLike()
    "storage", storageOptions
    "x", x ]

let serializerOptions =
  options
  |> List.map (fun (name, options) -> name, options.ToJsonSerializerOptions())

serializerOptions
|> List.iter (fun (name, options) ->
  let serialized =
    JsonSerializer.Serialize<Option<Option<string>>>(Some(None), options)

  System.IO.File.AppendAllText($"./test-option.json", name + ": " + serialized + "\n"))

Serilog.Log.Logger <-
  Serilog
    .LoggerConfiguration()
    .WriteTo.File("logs/log-.log", rollingInterval = RollingInterval.Day)
    .CreateLogger()

open Microsoft.FSharp.Reflection

module DiscriminatedUnionHelper =
  let GetAllUnionCases<'T> () =
    FSharpType.GetUnionCases(typeof<'T>)
    |> Seq.map (fun x -> FSharpValue.MakeUnion(x, Array.zeroCreate (x.GetFields().Length)) :?> 'T)

let serialize<'T> (v: 'T, serializerOptions: JsonSerializerOptions) =
  JsonSerializer.Serialize(v, serializerOptions)

let deserialize<'T> (v: string, serializerOptions: JsonSerializerOptions) =
  JsonSerializer.Deserialize<'T>(v, serializerOptions)

module Json =
  let options = storageOptions.ToJsonSerializerOptions()
  options.WriteIndented <- true
  let serialize x = serialize (x, options)
  let deserialize x = deserialize (x, options)


type Data = MyDomain.Invoicing.Core.Event

// let cases = FSharpType.GetUnionCases(typeof<'T>) |> Seq.map(fun x->x.Name)
let data = DiscriminatedUnionHelper.GetAllUnionCases<Data>()

printfn "Data %A" data

// https://www.planetgeek.ch/2023/03/31/todays-random-f-code-using-verify-to-prevent-breaking-changes-in-stored-data/

[<RequireQualifiedAccess>]
module Verify =

  let verify (value: obj) = Verifier.Verify(value).ToTask()

  let verify' (settings: VerifySettings) (value: obj) =
    Verifier.Verify(value, settings).ToTask()

  let verifyUsingParameters parameters (value: obj) =
    Verifier.Verify(value).UseParameters(parameters).ToTask()

  let verifyUsingParameters' parameters (settings: VerifySettings) (value: obj) =
    Verifier.Verify(value, settings).UseParameters(parameters).ToTask()

let verifySettings =
  let settings = VerifySettings()
  settings.UseDirectory "EventData"
  // settings.ModifySerialization (fun s ->
  //     s.DontScrubGuids ()
  //     s.DontScrubDateTimes ())
  settings.AddExtraSettings(fun settings ->

    // settings.Converters.Add()
    // settings.NullValueHandling <- nulLValueHan

    ())

  settings

VerifyDiffPlex.Initialize()
// [ModuleInitializer]
// public static void Initialize() =>
//     VerifyDiffPlex.Initialize();

type SingleCaseUnion1NoField = | SingleCase

type SingleCaseUnion2OfString = SingleCase of string

type SingleCaseUnion3OfMultipleFields = SingleCase of string * int * Guid

type Record =
  { Name: string
    Value: int
    Id: Guid
    Skipped: Skippable<string> }

type Du =
  | NoField
  | SingleCase of string
  | SingleRecordCase of Record
  | MultiCase of string * Record * int
  | MultiNamedCase of Name: string * Data: Record * Version: int
  | MutiCaseWithOption of string * Record option * int
  | MultiCaseWithSkippable of string * Skippable<Record> * Skippable<int>

let EventDataToSerialize: obj array array =
  let rec find (root: Assembly) : Assembly[] =
    let referencedRelevantAssemblies =
      root.GetReferencedAssemblies()
      |> Array.filter (fun a -> a.FullName.StartsWith "Calitime")
      |> Array.map (fun a -> Assembly.Load(a))
      |> Array.collect find
      |> Array.distinct

    Array.append [| root |] referencedRelevantAssemblies

  let relevantAssemblies = find typeof<Data>.Assembly

  // let types =
  //   relevantAssemblies
  //   |> Seq.collect(fun assembly->assembly.DefinedTypes)
  //   |> Seq.filter(fun t -> t.Name.EndsWith "Event")
  //   // maybe select event envelope here, and then the payload
  //   |> Seq.map(fun t -> t, (t.GetProperty "Data" |> Option.ofObj))
  //   |> Seq.choose(fun (t, field)->
  //     match field with
  //     | Some f -> Some(t, f)
  //     | None -> None
  //   )
  //   |> Seq.map(fun (t, field)-> t, field.PropertyType)
  //   |> Seq.toArray

  let types =
    [| ({| Name = "string option" |}, typeof<string option>)
       ({| Name = "Event" |}, typeof<MyDomain.Invoicing.Core.Event>)
       ({| Name = "SingleCaseDu1" |}, typeof<SingleCaseUnion1NoField>)
       ({| Name = "SingleCaseDu2" |}, typeof<SingleCaseUnion2OfString>)
       ({| Name = "SingleCaseDu3" |}, typeof<SingleCaseUnion3OfMultipleFields>)
       ({| Name = "Du" |}, typeof<Du>) |]

  let data =
    types
    |> Array.collect (fun (t, data) -> FSharpType.GetUnionCases data |> Array.map (fun info -> t, data, info))

  let data2 =
    data
    |> Array.map (fun (t, data, case) ->
      // let x = TinyEventStore.Creator.createUnionInstance [] relevantAssemblies [] case
      t, data, case, (TinyEventStore.Creator.createUnionInstance [] relevantAssemblies [] case))
    |> Array.map (fun (t, data, case, instance) -> [| $"{t.Name}-{data.Name}-{case.Name}" :> obj; instance |])

  data2

type EventDtoV1 = | Draft

// serializerOptions
// |> List.iter (fun (name, options) ->
//   let filename = $"./test-{name}.json"
//   System.IO.File.Delete(filename))

[<Theory>]
[<MemberData(nameof EventDataToSerialize)>]
let ``event data of all events`` (key: string, instance: obj) =
  task {
    // serializerOptions
    // |> List.iter (fun (name, options) ->
    //   let x = serialize (instance, options)
    //   let filename = $"./test-{name}.json"
    //   System.IO.File.AppendAllText(filename, $"'{key}': {x}\n")
    //   Serilog.Log.Logger.Information("{key} / {name}: {x}", key, name, x))

    let json = Json.serialize instance
    // encoder
    // let json = serialize(instance)
    let! _ = Verify.verifyUsingParameters' [| key :> obj; "" |] verifySettings json

    Assert.True(true)
  }

[<RequireQualifiedAccess>]
type InvoiceId =
  | InvoiceId of Guid

  static member New() = InvoiceId(Guid.NewGuid())
  static member ToRaw(InvoiceId id) = id
  static member FromRaw(id) = InvoiceId(id)

type CustomerId =
  | CustomerId of Guid

  static member New(value: Guid) = CustomerId value

type InvoiceNumber =
  | InvoiceNumber of string

  static member New(value: string) =
    if String.IsNullOrWhiteSpace value then
      failwith "invalid invoice number"
    else
      InvoiceNumber value

  static member ToRaw(InvoiceNumber id) = id
  static member FromRaw id = InvoiceNumber id

type DraftPosition =
  { Description: string option
    Quantity: int option
    Price: decimal option }

type InvoicePosition =
  { Description: string
    Quantity: int
    Price: decimal }

  member this.Total() = 0

type InvoiceDraft =
  { InvoiceNumber: InvoiceNumber option
    CustomerId: CustomerId option
    Positions: DraftPosition list }

// Final invoice
type Invoice =
  { InvoiceNumber: InvoiceNumber
    CustomerId: CustomerId
    CustomerName: string
    Positions: InvoicePosition list }

type EventDto0 =
  | DraftCreated of InvoiceDraft
  | DraftUpdated of InvoiceDraft
  | Finalized of Invoice

  static member ToEvent(dto: EventDto0) =
    match dto with
    | DraftCreated draft -> Event.DraftCreated draft
    | DraftUpdated draft -> Event.DraftUpdated draft
    | Finalized invoice -> Event.Finalized(invoice, None)

and EventDto1 =
  | DraftCreated of InvoiceDraft
  | DraftUpdated of InvoiceDraft
  | Finalized of Item: Invoice * Skippable<string option>

  static member ToEvent(dto: EventDto1) =
    match dto with
    | DraftCreated draft -> Event.DraftCreated draft
    | DraftUpdated draft -> Event.DraftUpdated draft
    | Finalized(invoice, html) -> Event.Finalized(invoice, html |> Skippable.defaultValue (Some "default value<br>"))

and Event =
  | DraftCreated of InvoiceDraft
  | DraftUpdated of InvoiceDraft
  | Finalized of Invoice * string option

let invoice =
  { Invoice.Positions = []
    CustomerId = CustomerId.New(Guid.Empty)
    InvoiceNumber = InvoiceNumber.New "5"
    CustomerName = "Customer 2" }

let verifiy (value: string) = Verifier.Verify(value).ToTask()

[<Fact>]
let ``test json serialization`` () =
  task {
    let e0 = EventDto0.Finalized invoice
    let e0serialized = Json.serialize e0

    let deserializedAsEvent1: EventDto1 = Json.deserialize e0serialized

    let evt = deserializedAsEvent1 |> EventDto1.ToEvent

    let! x = Verifier.Verify(e0serialized).ToTask()

    Assert.True(true)
  }
