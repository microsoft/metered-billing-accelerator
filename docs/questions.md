> This document uses the terms "aggregator" and "metered billing accelerator" synonymously. 

#### Why does the system send `ping` messages?

The event-sourcing-based business logic needs reliable and reproducible time information for state transitions. The most important use case is when to submit an aggregated usage to the Azure Metered Billing API: Let's say there have been three usage events at 10:02, 10:28 and 10:55. When would be the right moment in time to be sure that there can be no additional events coming in for the 10:00-11:00 time window? The right moment would be any time after 11:00.

In a distributed system, the hardware clocks from different computers cannot be absolutely synchronized. Therefore, the metered billing accelerator leverages the clock of the respective Azure Event Hubs partitions: Each message enqueued in a partition is time-stamped upon receipt, and that timestamp is stored in the metadata of the message (and also available in Event Hubs Capture).

When the business logic receives *any* message after 11:00, it knows that the Event Hubs partition will not have subsequent messages with a prior time stamp. Subsequent messages linearized in a partition have strictly monotonic increasing sequence numbers, offsets and timestamps. 

Without a `ping` message, the aggregated usage would be 'stuck' in the business logic, because it would not know whether it's missing further usage for the 10:55--11:00 timeframe. A running instance billing accelerator instance will ping itself (through Event Hubs) and ensure it can flush usage from the previous hour, even if the application itself doesn't generate further usage events.

In addition to regularly flushing  the previous hour's usage, it also can help identify service outages: If an Event Hubs partition isn't owned by a running instance, the absence of `ping` messages can help identifying such problems. 

`ping` messages are sent on a per-partition basis, i.e. each partition has it's own stream of ping messages flowing through the system.

An additional benefit: When re-processing a stream from Event Hubs capture, one has a good understanding at which point in time the system was in which state.

#### Suspend and reinstate scenarios will be handled?

You must ensure that the metered billing accelerator runs on a daily basis, ideally constantly. However, the system is designed with a primary focus on resiliency: You can forcefully stop (kill) a running aggregator instance at any point in time while its running; while it's aggregating usage, while HTTP requests with the Azure Metered Billing API are in flight, it just doesn't matter. 

How? Via Event Sourcing. Everything that results in state changes must flow through Event Hubs. The application sends all usage into Event Hubs. The metered billing accelerator sends all API responses from Azure into Event Hubs. 

A simple example (for details, check the [pattern](../pattern.md) document): Imagine the accelerator submits a usage event event request to the Azure REST API, but then the underlying compute crashed and the metered billing accelerator doesn't handle Azure's response. In this case, when the aggregator re-boots, it will re-send the request to Azure. Given that Azure received the request in the past already, it will respond with a `Duplicate` message. For the business logic, it doesn't matter whether the API response indicates `OK` or `Duplicate`; in both cases we know that Azure properly recorded the usage in the billing system.

#### How to remove unprocessed messages?

Authorized clients can send arbitrary payload into Event Hubs. In a properly installed and running system, all messages would be correctly formatted (so that the serializer can parse them), and all usage events would belong to existing SaaS subscriptions or managed apps. 

However, during development, it might happen that unprocessable messages get into Event Hub. To provide some visibility, the business logic stores these invalid messages in the state file (with their metadata). Sending a `RemoveUnprocessedMessages` message instructs the business logic to remove unprocessed messages from the state file. Check the [exactly](../src/Metering.Tests/data/InternalMessages/RemoveUnprocessedMessages%20exactly.json)  and [beforeIncluding](../src/Metering.Tests/data/InternalMessages/RemoveUnprocessedMessages%20exactly.json) samples. `exactly` removes a single message from a state file, based on an exact sequence number. The `beforeIncluding` removes all messages with a sequence number smaller or equal than the specified one.

These messages ensure debug traces can be removed from the state file.

#### Will the renewal Interval property take care of renewing the quantity monthly or annually? or any event needed to be raised?  When will the renewal be considered - the start of the renewal date(ex: 12 am GMT) 

A `SubscriptionPurchased` message contains the `subscriptionStart` and `renewalInterval` properties. The `renewalInterval` must be `Monthly` or `Annually`, and is used to determine when to 'replenish' the included quantities in a subscription. The current implementation does this based on the exact hour/minute/second, i.e. when the `subscriptionStart` of a monthly subscription is `"2021-11-04T16:12:26Z"`, then replenishment will happen `"2021-12-04T16:12:26Z"`, `"2022-01-04T16:12:26Z"`, etc., until the subscription is cancelled.

#### Retry interval in case of a failure while posting it to the metering API

When calls to the Azure Metered Billing API fail, the metered billing accelerator will retry with 1sec pauses; there's no exponential backoff etc. Implementation in `src/Metering.RuntimeCS/AggregationWorker.cs`. Simply speaking, all values that must be reported will stay in the system until they're successfully acknowledged. 

Technically, the `Metering.BaseTypes.Meter` type has a `UsageToBeReported: MarketplaceRequest list` property, which serves as a TODO list of values that still must be submitted.

#### After the subscription is canceled, how do we handle late metric usage events?  Ex: Session time: 10:00 am to 11:30 am, but the subscription canceled at 10:30 am 

When your application submits a `SubscriptionDeleted` message into the system, the business logic will submit all unsubmitted values to Azure, and remove the subscription from the system. If the application needs "cleanup time" of some sort, it has to send the `SubscriptionDeleted` event later. 

#### How do we handle the Change Plan scenario?

Currently not supported. What's a change plan scenario? 

#### Can we run the accelerator processor parallelly in a cluster?

The metered billing accelerator can run on a single node, or on multiple nodes. You can run it (literally) on a Raspberry Pi on your desk, or a VM Scale Set, or whatever compute can host .NET Core. 

The metered billing accelerator processes data from Azure Event Hub. When running on multiple nodes, it makes no sense to have more nodes than partitions in the Azure Event Hub. So the maximum reasonable number of nodes certainly is 32 or so. 

#### How often will the aggregator communicate with the marketplace API?

When the metered billing accelerator determines (based on a message timestamp) that the previous hour finished, all subscriptions that have reportable data will have events to be submitted. The aggregator / metered billing accelerator will batch events (up to 25 per call) and communicate with the marketplace API until everything is successfully submitted.

#### How can we store the processed state to the database from the aggregator?

The aggregator stores the system state as JSON files in blob storage. Each partition is handled in an own JSON file. 

#### How to monitor the request and response of the metering API?

All JSON responses from the metering API are written to Event Hub as well, so they show up in Event Hub Capture files.

#### How many snapshots can be created in a single partition? When a partition reaches a maximum limit, will a new partition be created?

Foreach partition in Event Hub, there is a snapshot file, for partition #2 for example something like `2/latest.json.gz`. Snapshots should be created at 'regular' intervals. The [`RegularlyCreateSnapshots()`](https://github.com/microsoft/metered-billing-accelerator/blob/82153924087237e109825a436cc15ed9c429812b/src/Metering.RuntimeCS/AggregationWorker.cs#L172-L180) function currently creates a snapshot for each event, but one could easily say only snapshot every 10, 50 or 100 events. 

If Event Hub Capture is enabled (and it really should), then it just doesn't matter. If the accelerator crashes / reboots, the worst thing that would happen is it resumes from an older snapshot, reprocesses 10 (or 50 or 100) messages, by reading them from Event Hub directly (or from capture), and eventually just tries submit some already submitted usage values.

#### If the size of a snapshot keeps on increasing, how does the aggregator handle those scenarios? (in case of numerous subscriptions)

The snapshots are .NET data structures, serialized as JSON. It has to fit into RAM of your node. Don't run it on an Arduino. 

#### Will the snapshots be cleared at any point in time? 

Currently, the [`storeLatestState`](https://github.com/microsoft/metered-billing-accelerator/blob/82153924087237e109825a436cc15ed9c429812b/src/Metering.Runtime/MeterCollectionStore.fs#L216) function writes the state 2 times, once in the `{partitionId}/latest.json.gz`  and second into a `{patitionId}/{datetime}---sequencenr-{sequenceNumber}.json.gz` file (for debugging purposes). You can reduce the number of snapshots (by only saving every X events), you can disable writing the file with the sequence number, or you could setup a job which deletes files older than a certain timestamp. 

The accelerator itself currently does not delete JSON snapshots.

If you accidentally delete all JSON in the container, no problem, just run the accelerator and it'll re-create everything. Event sourcing, baby.