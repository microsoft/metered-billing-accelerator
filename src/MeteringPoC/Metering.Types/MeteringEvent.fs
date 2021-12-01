namespace Metering.Types

open Metering.Types.EventHub

// TODO Can you refactor me away?
type MeteringEvent =
    { MeteringUpdateEvent: MeteringUpdateEvent
      MessagePosition: MessagePosition
      EventsToCatchup: EventsToCatchup }