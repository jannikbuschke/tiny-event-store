namespace TinyEventStore.Simple

open TinyEventStore.InterfacesSimple.Core
open TinyEventStore.InterfacesSimple
open System.Threading.Tasks
open FsToolkit.ErrorHandling

type Subscription<'state, 'e, 'ctx> = 'ctx -> AppendEventsResult<'state, 'e> -> Task<unit>

// this class is propably not needed
// just a function returning a function that accepts ctx or two, one for apply event, one for applyCommand
type EventStore<'state, 'e, 'c, 'ctx>
  (
    system: EventStoreDefinition<'state, 'e, 'c>,
    onCommitting: OnCommittingEventHandler<_, _, 'ctx> seq,
    subscriptions: Subscription<'state, 'e, 'ctx> list
  ) =
  let handler ctx = createHandler system onCommitting ctx
  member _.Rehydrate(store: ISimpleEventStorage<_, _, _>, id) =
    rehydrate system.aggregate store system.getEventVersion id

  member _.ApplyEvents
    (
      store: ISimpleEventStorage<'state, 'e, 'c>,
      id,
      events: CreateEventsContext -> NonEmptyList<'e>,
      ts,
      ctx: 'ctx,
      ?causation: TinyEventStore.Causation
    ) =
    taskResult {
      let applyEvents, _ = handler ctx
      let! result = applyEvents store (id, ts) events
      for subscription in subscriptions do
        do! subscription ctx result
      return result
    }

  member _.ApplyCommand(store: ISimpleEventStorage<'state, 'e, 'c>, (id, dt), command, ctx: 'ctx) =
    taskResult {
      let _, applyCommand = handler ctx
      let! result = applyCommand store (id, dt) command

      for subscription in subscriptions do
        do! subscription ctx result

      return result
    }


// module InmemEventStorage =
//
//   open System.Collections.Generic
//
//   let newStorage<'state, 'e, 'c>
//     =
//
//     let dict = Dictionary<'id, 'stream * ResizeArray<'e>>()
//
//     let tryGetVal (dict: IDictionary<_, _>) key =
//       match dict.TryGetValue key with
//       | true, v -> Some v
//       | false, _ -> None
//
//     let getEvents id =
//       id |> tryGetVal dict |> Option.map snd |> Option.map Seq.toList
//
//     let getStream id = id |> tryGetVal dict |> Option.map fst
//
//     let getEventStream id = id |> tryGetVal dict
//
//     { new IEventStorage<'state, 'e, 'c> with
//
//         member _.LoadEventRange(id, from: V, until: V) =
//           id
//           |> getEvents
//           |> Option.map (List.skip (int (from - 1L)))
//           |> Option.map (List.take (int (until - from)))
//           |> Task.FromResult
//
//         member _.QueryStreams() =
//           dict
//           |> Seq.map (fun id -> id.Key, id.Value |> fst)
//           |> Seq.toList
//           |> Ok
//           |> Task.FromResult
//
//         member _.LoadEventsFrom(id, from) =
//           id |> getEvents |> Option.map (List.skip (int (from - 1L))) |> Task.FromResult
//
//         member _.LoadAllEvents id =
//           id |> getEvents |> Option.map (List.skip 0) |> Task.FromResult
//
//         member _.LoadRequiredStream id =
//           taskResult {
//             let events, stream = id |> getEvents, id |> getStream
//             let! events =
//               events
//               |> Result.requireSome
//                 {
//                   Message = Some "Events not found"
//                   Details = EventStoreErrorDetails.NotFound
//                 }
//             let! stream =
//               stream
//               |> Result.requireSome
//                 {
//                   Message = Some "Stream not found"
//                   Details = EventStoreErrorDetails.NotFound
//                 }
//             return
//               {
//                 Id = id
//                 Stream = stream
//                 Events = events
//               }
//           }
//
//         member _.LoadStream id =
//           let events, stream = id |> getEvents, id |> getStream
//           Option.map2
//             (fun x y ->
//               {
//                 Id = id
//                 Stream = y
//                 Events = x
//               }
//             )
//             events
//             stream
//           |> Task.FromResult
//
//         member _.Commit v =
//           let existingStream =
//             match v.Id |> getEventStream with
//             | Some x -> x
//             | None ->
//               let list = ResizeArray()
//               let s = streamCreator v
//               s, list
//
//           let existingStream, existingEvents = existingStream
//           v.Events |> Seq.iter existingEvents.Add
//           // let lastEvent = v.Events |> Seq.last
//           let stream1 = streamUpdater existingStream v
//           dict[v.Id] <- stream1, existingEvents
//
//           () |> Ok |> Task.FromResult
// }
