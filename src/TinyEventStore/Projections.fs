module TinyEventStore.Projections

open System

[<CLIMutable>]
type EventProgression =
  { Name: string
    LastSeqId: int64 option
    LastUpdated: DateTimeOffset option }

let reset progression =
  { progression with
      LastSeqId = None
      LastUpdated = None }

let createProjection (name: string) =
  { Name = name
    LastSeqId = None
    LastUpdated = None }
