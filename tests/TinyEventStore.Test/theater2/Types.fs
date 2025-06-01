namespace Theater2

open System

[<RequireQualifiedAccess>]
type TheaterEventDetails =
  | Created of string
  | Updated of string
  | Deleted

type TheaterEvent =
  {
    StreamId: Guid
    Id: Guid
    Details: TheaterEventDetails
    Version: int64
    TimeStamp: DateTimeOffset
  }

type TheaterState =
  {
    Modified: DateTimeOffset
    Created: DateTimeOffset
    Name: string
    IsDeleted: bool
  }

type TheaterCommandDetails =
  | Create of string
  | Update of string
  | Delete

type TheaterCommand =
  {
    Id: Guid
    Details: TheaterCommandDetails
    TimeStamp: DateTimeOffset
  }

  static member New(v: TheaterCommandDetails, timestamp: DateTimeOffset) =
    {
      Id = Guid.NewGuid()
      TimeStamp = timestamp
      Details = v
    }
  static member New(v: TheaterCommandDetails) =
    TheaterCommand.New(v, DateTimeOffset.Now)
