namespace Theater

open System
open TinyEventStore.Interfaces

[<RequireQualifiedAccess>]
type TheaterStreamId =
  | TheaterStreamId of Guid
  static member New() = TheaterStreamId(Guid.NewGuid())
  static member ToRaw(TheaterStreamId id) = id
  static member FromRaw id = TheaterStreamId id
  static member FromRawString(input: string) = input |> Guid.Parse |> TheaterStreamId
  static member Converter = TheaterStreamId.ToRaw, TheaterStreamId.FromRaw

[<RequireQualifiedAccess>]
type TheaterEventDetails =
  | Created of string
  | Updated of string
  | Deleted

type TheaterEventEnvelope = TinyEventStore.EfStorage.EventEnvelope<TheaterStreamId,TheaterEventDetails>

// type TheaterEvent =
//   {
//     EventId: Guid
//     StreamId: TheaterStreamId
//     Version: V
//     TimeStamp: DateTimeOffset
//     Data: TheaterEventDetails
//   }
//
//   interface IEvent with
//     member this.Version = this.Version
//     member this.TimeStamp = this.TimeStamp

type TheaterStream =
  {
    Version: V
    Created: DateTimeOffset
    Updated: DateTimeOffset
    Name: string
  }

  interface IStream with
    member this.Version = this.Version
    member this.Modified = this.Updated
    member this.Created = this.Created

type TheaterState =
  {
    TimeStamp: DateTimeOffset
    Name: string
    IsDeleted: bool
  }

type TheaterCommandDetails =
  | Create of string
  | Update of string
  | Delete

type TheaterCommand =
  {
    TimeStamp: DateTimeOffset
    Data: TheaterCommandDetails
  }

  static member New(v: TheaterCommandDetails, timestamp: DateTimeOffset) =
    {
      TimeStamp = timestamp
      Data = v
    }
  static member New(v: TheaterCommandDetails) =
    {
      TimeStamp = DateTimeOffset.Now
      Data = v
    }

  interface ICommand with
    member this.TimeStamp = this.TimeStamp
