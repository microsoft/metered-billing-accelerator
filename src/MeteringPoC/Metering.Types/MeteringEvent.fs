namespace Metering.Types

open Metering.Types.EventHub

type MeteringEvent =
    { MeteringUpdateEvent: MeteringUpdateEvent
      MessagePosition: MessagePosition }