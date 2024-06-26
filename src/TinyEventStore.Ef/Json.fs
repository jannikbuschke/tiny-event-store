module TinyEventStore.Json

open System.Text.Json
open System.Text.Json.Serialization

let serializerOptions =
  JsonFSharpOptions(
    JsonUnionEncoding.AdjacentTag
    ||| JsonUnionEncoding.NamedFields
    ||| JsonUnionEncoding.UnwrapOption
    // ||| JsonUnionEncoding.UnwrapSingleCaseUnions
    ||| JsonUnionEncoding.AllowUnorderedTag
  )
    .ToJsonSerializerOptions()

let serialize<'T> (v: 'T) =
  JsonSerializer.Serialize(v, serializerOptions)

let deserialize<'T> (v: string) =
  JsonSerializer.Deserialize<'T>(v, serializerOptions)

let convert<'t> = (serialize<'t>),(deserialize<'t>)
