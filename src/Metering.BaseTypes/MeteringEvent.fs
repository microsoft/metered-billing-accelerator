// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.BaseTypes

type MeteringEvent =
    | Remote of Event:MeteringUpdateEvent * MessagePosition:MessagePosition * EventsToCatchup:EventsToCatchup option
    | Local of Event:LocalControlEvent

module MeteringEvent =
    let create (e: MeteringUpdateEvent) (messagePosition: MessagePosition) (eventsToCatchup: EventsToCatchup option) : MeteringEvent =
        Remote (Event = e, MessagePosition = messagePosition, EventsToCatchup = eventsToCatchup)

    let fromEventHubEvent (e: EventHubEvent<MeteringUpdateEvent>) : MeteringEvent =
        Remote (Event = e.EventData, MessagePosition = e.MessagePosition, EventsToCatchup = e.EventsToCatchup)

    let createLocalEvent (e: LocalControlEvent) : MeteringEvent = 
        Local (Event = e)
