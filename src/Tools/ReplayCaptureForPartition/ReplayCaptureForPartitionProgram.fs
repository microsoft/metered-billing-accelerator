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

let writeToFilesystem dirname (newState: MeterCollection) (msg: EventHubEvent<MeteringUpdateEvent>) =
    File.WriteAllText((sprintf "%s\\%s-%d-msg-%s.json" dirname newState.LastUpdate.Value.PartitionID.value newState.LastUpdate.Value.SequenceNumber (msg.EventData.MessageType)), (Json.toStr 1 msg.EventData))
    File.WriteAllText((sprintf "%s\\%s-%d-state.json" dirname newState.LastUpdate.Value.PartitionID.value newState.LastUpdate.Value.SequenceNumber), (Json.toStr 1 newState))

let handle dirname state msg = 
    let newState = MeterCollectionLogic.handleMeteringEvent state msg
    
    writeToFilesystem dirname newState msg

    newState


config.createEventHubConsumerClient().GetPartitionIdsAsync(cancellationToken).Result
|> Seq.map PartitionID.create
|> Seq.iter(fun partitionId -> 
    let dirname = $"data\\{config.EventHubConfig.EventHubName.NamespaceName}\\{config.EventHubConfig.EventHubName.InstanceName}\\{partitionId.value}"
    let dirname = Directory.CreateDirectory(dirname).FullName

    let events = CaptureProcessor.readAllEvents CaptureProcessor.toMeteringUpdateEvent partitionId cancellationToken config
    
    let finalState = events |> Seq.fold (handle dirname) MeterCollection.Empty

    printfn "%A" finalState
)

exit 0
