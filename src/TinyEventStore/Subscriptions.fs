module TinyEventStore.Subscriptions
open System.Threading.Channels
open TinyEventStore.Simple

let createChannelSubscription<'e, 'ctx, 'state>(): Channel<'e> * Subscription<'ctx,'e, 'state> =
  let channel = System.Threading.Channels.Channel.CreateUnbounded<'e>()
  let eventToChannelSubscription: ChannelWriter<'e> -> Subscription<_, 'e, _> =
    fun channel _ v -> task {
        for e in v.Events.List do
          do! channel.WriteAsync e
        return ()
      }
  channel, (eventToChannelSubscription channel.Writer)
