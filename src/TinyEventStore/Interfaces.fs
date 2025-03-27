namespace TinyEventStore.Interfaces

type IEvent =
  abstract member Version: uint64

type Evolve<'s, 'e> = 's -> 'e -> 's
type Decide<'s, 'c, 'e> = 's -> 'c -> 'e list
type IsDeleted<'s, 'e> = 's -> 'e -> bool

type Aggregate<'s, 'e> =
  { zero: 's
    evolve: Evolve<'s, 'e>
    isDeleted: IsDeleted<'s, 'e> }

type System<'s, 'e, 'c> =
  { aggregate: Aggregate<'s, 'e>
    decide: Decide<'s, 'c, 'e> }


open System.Threading.Tasks
open System.Linq

// type IEventStore<'event  where 'event :> IEvent> =
type IEventStore<'event> =
  abstract member LoadEventRange: uint64 * uint64 -> Task<'event seq>
  abstract member LoadEventsFrom: (uint64) -> Task<'event seq>
// abstract member this.LoadEventsUntil:(from:uint64) -> Task<'event seq>

type InmemEventStore<'event>() =
  let events = ResizeArray()

  interface IEventStore<'event> with
    member this.LoadEventRange(from: uint64, until: uint64) =
      //failwith ""
      //[] |> List.toSeq |> Task.FromResult
      events.Skip(int (from - 1UL)).Take(int (until - from)) |> Task.FromResult

    member this.LoadEventsFrom(from) =
      events.Skip(int (from - 1UL)) |> Task.FromResult
