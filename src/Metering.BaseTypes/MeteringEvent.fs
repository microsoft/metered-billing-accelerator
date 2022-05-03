// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.BaseTypes

open Metering.BaseTypes.EventHub

type MeteringEvent =
    { Event: MeteringUpdateEvent
      MessagePosition: MessagePosition 
      EventsToCatchup:EventsToCatchup option }

    static member create (e: MeteringUpdateEvent) (messagePosition: MessagePosition) (eventsToCatchup: EventsToCatchup option) : MeteringEvent =
        { Event = e
          MessagePosition = messagePosition
          EventsToCatchup = eventsToCatchup }

    static member fromEventHubEvent (e: EventHubEvent<MeteringUpdateEvent>) : MeteringEvent =
        { Event = e.EventData
          MessagePosition = e.MessagePosition
          EventsToCatchup = e.EventsToCatchup }

