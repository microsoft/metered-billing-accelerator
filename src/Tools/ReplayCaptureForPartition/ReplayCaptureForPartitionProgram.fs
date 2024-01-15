open System.IO
open System.Threading
open Metering.BaseTypes
open Metering.BaseTypes.EventHub
open Metering.EventHub
open Metering.Integration

//
// This utility reads the complete EventHub capture for a specific partition (all events since beginning of time),
// and stores the events and the state after applying a certain event, as JSON files in the file system.
//

let cancellationToken =CancellationToken.None
let config = MeteringConnections.getFromEnvironment()

let writeToFilesystem dirname (msg: EventHubEvent<MeteringUpdateEvent>) (newState: MeterCollection) =
    File.WriteAllText((sprintf "%s\\%s-%d-%s-msg-%s.json" dirname newState.LastUpdate.Value.PartitionID.value newState.LastUpdate.Value.SequenceNumber (msg.MessagePosition.PartitionTimestamp |> MeteringDateTime.blobName) (msg.EventData.MessageType)), (Json.toStr 1 msg.EventData))
    File.WriteAllText((sprintf "%s\\%s-%d-%s-state.json"  dirname newState.LastUpdate.Value.PartitionID.value newState.LastUpdate.Value.SequenceNumber (msg.MessagePosition.PartitionTimestamp |> MeteringDateTime.blobName)), (Json.toStr 1 newState))
    newState

let handle (dirname: string) (state: MeterCollection) (msg: EventHubEvent<MeteringUpdateEvent>) : MeterCollection =
    msg
    |> MeterCollectionLogic.handleMeteringEvent state
    |> writeToFilesystem dirname msg

config.createEventHubConsumerClient().GetPartitionIdsAsync(cancellationToken).Result
|> Seq.map PartitionID.create
|> Seq.iter(fun partitionId ->
    let dirname = $"data\\{config.EventHubConfig.EventHubName.NamespaceName}\\{config.EventHubConfig.EventHubName.InstanceName}\\{partitionId.value}"
    let dirname = Directory.CreateDirectory(dirname).FullName

    let events = CaptureProcessor.readAllEvents CaptureProcessor.toMeteringUpdateEvent partitionId cancellationToken config

    events
    |> Seq.fold (handle dirname) MeterCollection.Empty
    |> (fun state ->
        if state <> MeterCollection.Empty
        then printfn "%A" state)
)

exit 0
