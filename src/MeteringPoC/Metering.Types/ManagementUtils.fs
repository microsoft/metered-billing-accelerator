namespace Metering

open System.Threading
open Metering
open Metering.Types
open Metering.Types.EventHub

module ManagementUtils =
    let recreateLatestStateFromEventHubCapture (config : MeteringConfigurationProvider) (partitionId: PartitionID)  =
        let cancellationToken = CancellationToken.None

        let aggregate = MeterCollectionLogic.handleMeteringEvent config
        let initialState : MeterCollection option = MeterCollection.Uninitialized

        let x = 
            config.MeteringConnections
            |> CaptureProcessor.readAllEvents EventHubObservableClient.toMeteringUpdateEvent (partitionId |> PartitionID.value) cancellationToken 
            |> Seq.map MeteringEvent.fromEventHubEvent
            |> Seq.scan aggregate MeterCollection.Empty
            |> Seq.last

        (MeterCollectionStore.storeLastState config x cancellationToken).Wait()

    //let showEventsFromPositionInEventHub  (config : MeteringConfigurationProvider) (partitionId: PartitionID) =
    //    CaptureProcessor.readEventsFromPosition 
    //    config.MeteringConnections
    //    |> CaptureProcessor.readCaptureFromPosition CancellationToken.None  EventHubObservableClient.toMeteringUpdateEvent (partitionId |> PartitionID.value) cancellationToken 

    let getUnsubmittedMeters (config : MeteringConfigurationProvider) (partitionId: PartitionID) (cancellationToken: CancellationToken) =
        task {
            let! state = 
                MeterCollectionStore.loadLastState 
                    config partitionId cancellationToken

            return
                match state with
                | Some m -> m |> MeterCollection.metersToBeSubmitted
                | None -> Seq.empty            
        }
