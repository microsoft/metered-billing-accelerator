# 2022-12-22 Ping messages

The business logic can only "move forward" on a new message coming out of Event Hub. The reason is that the `EnqueuedTime` of an EventHub message serves as our source of truth on what time it is. 

Imagine we have an hour during which usage events create overage; between 13:00 and 13:59, there have been multiple usage events that result in an overage to be submitted to the Azure metering API. To be submitted to the metering API, the aggregator needs to be sure that no further usage can come in for the 13:00--13:59 time period. This is only guaranteed by receiving an event from 14:00+ onwards. However, if there would be no additional events coming into the EventHub partition, then the usage would be "stuck" in the meter, without being elevated to become a `MarketplaceRequest` in the `UsageToBeReported` collection. 

To promote overage from the previous hour to become a concrete  `MarketplaceRequest` in the `UsageToBeReported` collection, the aggregator must ensure the Business logic has up-to-date timing information. Unfortunately, there is no API in EventHub that would allow us to query an EventHub partition's internal clock. We can turn on `EventProcessorClient.ClientOptions.TrackLastEnqueuedEventProperties ` and configure `EventProcessorClient.ClientOptions.MaximumWaitTime ` to emit empty notifications, but unfortunately, reading the  `LastEnqueuedEventProperties` on an empty notification doesn't give us proper timing information.

The solution to the problem of getting reliable partition timestamp information into the business logic therefore is to have the aggregator 'regularly' send a `Ping` message into each partition it owns. There are 2 types of Ping messages:

- The `PingMessage.PingReason == ProcessingStarting` is sent when the aggregator starts owning a certain partition.
- The `PingMessage.PingReason == TopOfHour` is sent shortly after each new hour (when the aggregator can be reasonably confident that each event hub partition's compute node's clock is also in the new hour).



