// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.Utils

open System.Threading
open System.Threading.Tasks
open System.Runtime.CompilerServices
open Metering.EventHub
open Metering.BaseTypes
open Metering.BaseTypes.EventHub
open Metering.Integration

type ReportingOverview =
    { ApplicationInternalName: string
      LastUpdate: MeteringDateTime
      RemainingIncluded: Quantity
      TotalConsumed: Quantity }

module ReportingOverview =
    let fromBillingDimensions (dimensions: BillingDimensions) : ReportingOverview list =
            dimensions
            |> Map.toSeq
            |> Seq.map (fun (appInternalName, billingDimension) ->
                match billingDimension with
                | SimpleBillingDimension d ->
                    match d.Meter with
                    | None -> None
                    | Some m ->
                        match m with
                        | IncludedQuantity q ->
                            {
                                ApplicationInternalName = appInternalName.value
                                TotalConsumed = Quantity.Zero
                                RemainingIncluded = q.RemainingQuantity
                                LastUpdate = q.LastUpdate
                            } |> Some
                        | ConsumedQuantity q ->
                            {
                                ApplicationInternalName = appInternalName.value
                                TotalConsumed = q.BillingPeriodTotal
                                RemainingIncluded = Quantity.Zero
                                LastUpdate = q.LastUpdate
                            } |> Some
                | WaterfallBillingDimension d ->
                    match d.Meter with
                    | None -> None
                    | Some m ->
                        {
                            ApplicationInternalName = appInternalName.value
                            TotalConsumed = m.Total
                            RemainingIncluded = Quantity.Zero
                            LastUpdate = m.LastUpdate
                        } |> Some
            )
            |> Seq.choose id
            |> Seq.toList

[<Extension>]
module ManagementUtils =
    [<Extension>]
    let recreateStateFromEventHubCapture (config: MeteringConfigurationProvider) (messagePosition: MessagePosition) : MeterCollection =
        config.MeteringConnections
        |> CaptureProcessor.readAllEvents CaptureProcessor.toMeteringUpdateEvent messagePosition.PartitionID CancellationToken.None
        |> Seq.filter (fun e -> e.MessagePosition.SequenceNumber < messagePosition.SequenceNumber)
        |> Seq.scan MeterCollectionLogic.handleMeteringEvent MeterCollection.Empty
        |> Seq.last

    [<Extension>]
    let recreateLatestStateFromEventHubCapture (connections: MeteringConnections) (partitionId: PartitionID)  =
        let cancellationToken = CancellationToken.None

        let x =
            connections
            |> CaptureProcessor.readAllEvents CaptureProcessor.toMeteringUpdateEvent partitionId cancellationToken
            |> Seq.scan MeterCollectionLogic.handleMeteringEvent MeterCollection.Empty
            |> Seq.last

        (MeterCollectionStore.storeLastState connections x cancellationToken).Wait()

    [<Extension>]
    let showEventsFromPositionInEventHub (config: MeteringConfigurationProvider) (partitionId: PartitionID) (start: MeteringDateTime) =
        config.MeteringConnections
        |> CaptureProcessor.readEventsFromTime CaptureProcessor.toMeteringUpdateEvent partitionId start CancellationToken.None

    let getUnsubmittedMeters (meteringConnections: MeteringConnections) (partitionId: PartitionID) (cancellationToken: CancellationToken) : Task<MarketplaceRequest seq> =
        task {
            let! state =
                MeterCollectionStore.loadLastState
                    meteringConnections partitionId cancellationToken

            return
                match state with
                | Some m -> m.MetersToBeSubmitted()
                | None -> Seq.empty

        }

    [<Extension>]
    let getPartitionCount (connections: MeteringConnections) (cancellationToken: CancellationToken) : Task<string seq> =
        task {
            let! props = connections.createEventHubConsumerClient().GetEventHubPropertiesAsync(CancellationToken.None)
            return props.PartitionIds |> Array.toSeq
        }

    let getConfigurations (connections: MeteringConnections) (cancellationToken: CancellationToken) : Task<Map<PartitionID, MeterCollection>> =
        //let aMap f x = async {
        //    let! a = x
        //    return f a }

        task {
            let! partitionIDs = getPartitionCount connections cancellationToken

            let fetchTasks =
                partitionIDs
                |> Seq.map PartitionID.create
                |> Seq.map (fun partitionId -> MeterCollectionStore.loadLastState connections partitionId cancellationToken)
                |> Seq.toArray

            let! x = Task.WhenAll<MeterCollection option>(fetchTasks)

            return
                x
                |> Array.choose id
                |> Array.map (fun x -> (x.LastUpdate.Value.PartitionID, x))
                |> Map.ofArray
        }

    let getMetersForSubscription  (connections: MeteringConnections) (resourceId: MarketplaceResourceId) (cancellationToken: CancellationToken) : Task<ReportingOverview list> =
        task {
            let! allMeterCollections = getConfigurations connections cancellationToken

            return
                allMeterCollections
                |> Map.toSeq
                |> Seq.map (fun (_partitionId, meterCollection) -> meterCollection)
                |> Seq.collect (fun y -> y.Meters)
                |> Seq.tryFind (fun y -> y.Subscription.MarketplaceResourceId.Matches resourceId)
                |> function
                    | None -> List.empty
                    | Some y -> y.Subscription.Plan.BillingDimensions |> ReportingOverview.fromBillingDimensions
        }