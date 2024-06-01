module TinyEventStore.Creator

//based on https://www.planetgeek.ch/2021/04/27/type-safety-across-net-and-typescript-testing-json-serialization-and-deserialization/

open System.Reflection
open System
open Microsoft.FSharp.Reflection

type CustomCreator = (Type -> bool) * (Type -> (Type -> obj) -> obj)

type ArrayCreator =
  static member Create<'a>(value: obj option) =
    match value with
    | Some v ->
      let casted: 'a = downcast v
      let a: 'a[] = [| casted |]
      a
    | None -> [||]

type ListCreator =
  static member Create<'a>(value: obj option) =
    match value with
    | Some v ->
      let casted: 'a = downcast v
      let a: 'a list = [ casted ]
      a
    | None -> []

let rec private createRecordInstance
  (customCreators: CustomCreator list)
  (assemblies: Assembly[])
  (loopBreaker: Type list)
  (t: Type)
  =
  let constructors: ConstructorInfo[] =
    t.GetConstructors(BindingFlags.Public ||| BindingFlags.Instance)

  if (constructors.Length = 0) then
    failwith $"no public constructors on ${t.Name}"

  let constructor = constructors |> Array.minBy (fun c -> c.GetParameters().Length)

  let parameters = constructor.GetParameters()

  let arguments =
    parameters
    |> Array.map (fun p -> createInstance customCreators assemblies loopBreaker p.ParameterType)

  let instance = constructor.Invoke arguments

  instance

and private createInstance
  (customCreators: CustomCreator list)
  (assemblies: Assembly[])
  (loopBreaker: Type list)
  (t: Type)
  =
  let loopBreaker = t :: loopBreaker

  let customCreator =
    customCreators
    |> List.tryFind (fun (matcher, _) -> matcher t)
    |> Option.map (fun (_, c) -> c)

  match customCreator with
  | Some c -> c t (createInstance customCreators assemblies loopBreaker)
  | None ->
    try
      if
        t.IsGenericType
        && t.GetGenericTypeDefinition() = typedefof<FSharp.Collections.list<_>>
      then
        let elementType = t.GetGenericArguments() |> Array.exactlyOne

        let innerValue =
          if loopBreaker |> List.contains elementType then
            None
          else
            createInstance customCreators assemblies loopBreaker elementType |> Some

        let generationMethod =
          typeof<ListCreator>.GetMethod("Create").MakeGenericMethod(elementType)

        let value = generationMethod.Invoke(null, [| innerValue |])

        value
      elif t.IsGenericType && t.GetGenericTypeDefinition() = typedefof<Option<_>> then
        None :> obj
      elif
        t.IsGenericType
        && t.GetGenericTypeDefinition() = typedefof<System.Collections.Generic.IEnumerable<_>>
        || t.IsGenericType
           && t.GetGenericTypeDefinition() = typedefof<System.Collections.Generic.IReadOnlyCollection<_>>
        || t.IsGenericType
           && t.GetGenericTypeDefinition() = typedefof<System.Collections.Generic.IReadOnlyList<_>>
      then
        let elementType = t.GetGenericArguments() |> Array.exactlyOne

        let innerValue =
          if loopBreaker |> List.contains elementType then
            None
          else
            createInstance customCreators assemblies loopBreaker elementType |> Some

        let generationMethod =
          typeof<ArrayCreator>.GetMethod("Create").MakeGenericMethod(elementType)

        let value = generationMethod.Invoke(null, [| innerValue |])

        value
      elif t.IsArray then
        let elementType = t.GetElementType()

        let innerValue =
          if loopBreaker |> List.contains elementType then
            None
          else
            createInstance customCreators assemblies loopBreaker elementType |> Some

        let generationMethod =
          typeof<ArrayCreator>.GetMethod("Create").MakeGenericMethod(elementType)

        let value = generationMethod.Invoke(null, [| innerValue |])

        value
      elif FSharpType.IsUnion t then
        t
        |> FSharpType.GetUnionCases
        |> Array.head
        |> createUnionInstance customCreators assemblies loopBreaker
      elif FSharpType.IsRecord t then
        if t.IsGenericType then
          failwithf "could not create an instance of type %s because it is generic" t.FullName
        else
          createRecordInstance customCreators assemblies loopBreaker t
      elif t = typeof<string> then
        "value" :> obj
      elif t = typeof<int> then
        42 :> obj
      elif t = typeof<int64> then
        1234567890123456789L :> obj
      elif t = typeof<bool> then
        true :> obj
      elif t = typeof<Guid> then
        Guid("12345678-1234-1234-1234-123456781234") :> obj
      // elif t = typeof<LocalDate> then
      //     LocalDate(2020, 11, 27) :> obj
      // elif t = typeof<LocalTime> then
      //     LocalTime(10, 9, 0) :> obj
      // elif t = typeof<LanguageKey> then
      //     LanguageKey("xy") :> obj
      elif t.IsInterface || t.IsAbstract then
        let implementation =
          assemblies
          |> Seq.collect (fun a -> a.GetTypes())
          |> Seq.tryFind (fun x -> t.IsAssignableFrom x && not (x = t) && not x.IsAbstract && not x.IsInterface)

        match implementation with
        | Some i -> createInstance customCreators assemblies loopBreaker i
        | None -> failwith $"could not find an implementation of abstract type/interface %s{t.FullName}"
      elif t.IsEnum then
        Enum.GetValues(t).GetValue(0)
      else
        createRecordInstance customCreators assemblies loopBreaker t
    with e ->
      failwithf $"could not create an instance of type %s{t.FullName}: exception message: %s{e.Message}"

and createUnionInstance
  (customCreators: CustomCreator list)
  (assemblies: Assembly[])
  (loopBreaker: Type list)
  (c: UnionCaseInfo)
  =
  let fieldTypes = c.GetFields() |> Array.map (fun p -> p.PropertyType)

  let arguments =
    fieldTypes |> Array.map (createInstance customCreators assemblies loopBreaker)

  FSharpValue.MakeUnion(c, arguments)

let create (customCreators: CustomCreator list) (assemblies: Assembly[]) (t: Type) =
  if FSharpType.IsUnion t then
    let cases = FSharpType.GetUnionCases t

    cases
    |> Array.map (fun c ->
      (t.Assembly.GetName().Name + t.Name + c.Name, createUnionInstance customCreators assemblies [] c))
    |> Array.toList
  else
    [ (t.Assembly.GetName().Name + t.Name, createInstance customCreators assemblies [] t) ]
