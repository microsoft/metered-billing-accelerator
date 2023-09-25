# What's new?

## Version 1.1

- **2023-09-18**: Added a configuration option for controlling the frequency of state snapshot creation in blob storage. The environment variables `AZURE_METERING_MAX_DURATION_BETWEEN_SNAPSHOTS` and `AZURE_METERING_MAX_NUMBER_OF_EVENTS_BETWEEN_SNAPSHOTS` can be used to specify how often a snapshot is created (which ever comes first): For example, `AZURE_METERING_MAX_DURATION_BETWEEN_SNAPSHOTS="00:05:00"` and `AZURE_METERING_MAX_NUMBER_OF_EVENTS_BETWEEN_SNAPSHOTS="2000"` ensure that at least every 5 minutes or every 2000 processed events, a new snapshot is created. 
- **2023-09-14**: Added a diagnostics tool (`src/Tools/ReprocessLocalEventHubCaptureFiles`) which can be used to replay and analyze locally downloaded Event Hub Capture files. 
- **2023-09-14:** Added an option to `RemoveUnprocessedMessages` which purges *all* unprocessed messages from a partition's state file: `{ "type": "RemoveUnprocessedMessages", "value": { "partitionId": "5", "all": "all" } }`
- **2023-09-14:** Added support for 2-year and 3-year SaaS offers, i.e., the `RenewalInterval` can not only take the values `Monthly` and `Annually`, but also `2-years` and `3-years`.
- **2023-08-31:** Added business logic unit test completely based on JSON files. For example, [src/Metering.Tests/data/BusinessLogic/RefreshIncludedQuantities](https://github.com/microsoft/metered-billing-accelerator/tree/main/src/Metering.Tests/data/BusinessLogic/RefreshIncludedQuantities) contains a full series of events and the state files resulting from applying each event. The event's JSON filename must contain the EventHubs timestamp. All files must contain the sequence number. Check the  [README.md in the `src/Metering.Tests/data/BusinessLogic` folder](https://github.com/microsoft/metered-billing-accelerator/tree/main/src/Metering.Tests/data/BusinessLogic) 

  