namespace TinyEventStore.InmemoryStorage

open System.Collections.Generic
open TinyEventStore.Interfaces
open FsToolkit.ErrorHandling
open System
open Core

// type EventEnvelope<'id, 'details> =
//   {
//     StreamId: 'id
//     EventId: TinyEventStore.EventId
//     Details: 'details
//     Version: V
//     TimeStamp: DateTimeOffset
//     Causation: TinyEventStore.Causation option
//   }
//   interface IEvent with
//     member this.TimeStamp = this.TimeStamp
//     member this.Version = this.Version

type Stream<'id> =
  {
    Id: 'id
    Version: V
    Created: DateTimeOffset
    Modified: DateTimeOffset
  }

  interface IStream with
    member this.Version = this.Version
    member this.Modified = this.Modified
    member this.Created = this.Created

open TinyEventStore.Log
// open TinyEventStore.Log.Types

module Result =
  let iterError f x =
    match x with
    | Ok _ -> ()
    | Error e -> f e

type InmemoryStorage<'streamId, 'state, 'event, 'c when 'streamId: equality and 'event :> IEvent>() =

  let logger = LogProvider.getLoggerByName "TinyEventStore.Inmemory"

  let data = Dictionary<'streamId, Stream<'streamId> * ResizeArray<'event>>()

  // let toEventEnvelope  (version,eventId,streamId,timestamp) (dto:'eventDetails)=
  //     // logger.warn(Log.setMessage "to event envelope")|>ignore
  //     if box dto = null then
  //       failwith "dto is null"
  //     else
  //       {
  //         StreamId = streamId
  //         EventId = eventId
  //         Details = dto
  //         Version = version
  //         TimeStamp = timestamp
  //         Causation = None
  //       }

  let createStream: StreamCreator<_, _, _, _> =
    fun appendEventsResult ->
      {
        Version = appendEventsResult.Version
        Created = appendEventsResult.TimeStamp
        Modified = appendEventsResult.TimeStamp
        Id = appendEventsResult.Id
      }

  let updateStream: StreamUpdater<_, _, _, _> =
    fun stream0 appendEventsResult ->
      { stream0 with
          Version = appendEventsResult.Version
          Modified = appendEventsResult.TimeStamp
      }

  interface IEventStorage<'streamId, Stream<'streamId>, 'state, 'event, 'c> with

    member this.LoadEventRange(id: 'streamId, from, until) =
      let events = data.[id]|>snd
      events |> Seq.toList |> Some |> Threading.Tasks.Task.FromResult

    member this.LoadEventsFrom(id, from) =
      let events = data.[id]|>snd
      events |> Seq.toList |> Some |> Threading.Tasks.Task.FromResult

    member this.LoadAllEvents id =
      let events = data.[id]|>snd
      events |> Seq.toList |> Some |> Threading.Tasks.Task.FromResult

    member this.LoadStream(id: 'streamId) =
        logger.info (Log.setMessage "Loading stream {stream_id}" >> Log.addParameter id)
        match data.TryGetValue id with
        | true, (stream,events) ->
          // let stream,events = data.[id]
          {
            Id = id
            Stream = stream
            Events = events |> Seq.toList
          }
          |> Some
        | false,_ -> None

        |> Threading.Tasks.Task.FromResult

    member this.LoadRequiredStream(id: 'streamId) =
      task {
        let! stream = (this :> IEventStorage<_, _, _, _, _>).LoadStream id
        let stream =
          stream
          |> Result.requireSome (EventStoreError.New(EventStoreErrorDetails.NotFound, None))
        stream
        |> Result.iterError (fun e ->
          logger.error (
            Log.setMessage "Loading required stream {stream} errored {error}"
            >> Log.addParameter id
            >> Log.addParameter e
          )
        )
        return stream
      }

    member this.Commit v =
      taskResult {
        logger.debug (
          Log.setMessage "Committing events. Stream is new = {isNew}"
          >> Log.addParameter v.IsNew
          >> Log.addContext "Events" v.Events
        )
        let! stream = (this :> IEventStorage<_, _, _, _, _>).LoadStream v.Id

        let! s1 =
          if v.IsNew then
            createStream v |> Ok
          else
            if stream.IsNone then
              logger.error (Log.setMessage "v.IsNew but no stream found")
            stream
            |> Option.map (fun x -> updateStream x.Stream v |> Ok)
            |> Option.defaultValue (EventStoreError.New(EventStoreErrorDetails.NotFound, None) |> Error)

        if v.IsNew then
          data.Add(v.Id, (s1, ResizeArray<'event>()))

        let eventDtos = v.Events //|> List.map (toEventEnvelope (0UL,))

        let stream, storedEvents = data.[v.Id]
        eventDtos |> Seq.iter (storedEvents.Add >> ignore)

        data.Remove(v.Id) |> ignore
        data.Add(v.Id, (s1, storedEvents))

        return ()
      }

    member this.QueryStreams() = failwith "todo"

  member this.LoadAllEvents id =
    (this :> IEventStorage<_, _, _, _, _>).LoadAllEvents id
