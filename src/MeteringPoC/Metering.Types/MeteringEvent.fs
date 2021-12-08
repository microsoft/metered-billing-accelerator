namespace Metering.Types

open Metering.Types.EventHub

// TODO Can you refactor me away?
type MeteringEvent =
    { MeteringUpdateEvent: MeteringUpdateEvent
      MessagePosition: MessagePosition
      EventsToCatchup: EventsToCatchup option }

module MeteringEvent =
    let create (meteringUpdateEvent: MeteringUpdateEvent) (messagePosition: MessagePosition) (eventsToCatchup: EventsToCatchup option) : MeteringEvent =
        { MeteringUpdateEvent = meteringUpdateEvent
          MessagePosition = messagePosition
          EventsToCatchup = eventsToCatchup }