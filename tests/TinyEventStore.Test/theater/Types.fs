namespace Theater

open System
open TinyEventStore.Interfaces

type EventDetails =
  | Created of string
  | Updated of string
  | Deleted

type TheaterEvent =
  { Version: uint64
    TimeStamp: DateTimeOffset
    Data: EventDetails }

  interface IEvent with
    member this.Version = this.Version

type TheaterState =
  { TimeStamp: DateTimeOffset
    Name: string
    IsDeleted: bool }


type TheaterCommandDetails =
  | Create of string
  | Update of string
  | Delete


type TheaterCommand =
  { TimeStamp: DateTimeOffset
    Data: TheaterCommandDetails }
