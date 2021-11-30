namespace Metering.Types

open Metering.Types.EventHub

// TODO delete me
type MeteringEvent =
    { MeteringUpdateEvent: MeteringUpdateEvent
      MessagePosition: MessagePosition
      EventsToCatchup: EventsToCatchup }