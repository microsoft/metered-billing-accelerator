// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.BaseTypes

open System.Runtime.CompilerServices
open Metering.BaseTypes.EventHub

type MeterCollection = 
    { MeterCollection: Map<MarketplaceResourceId, Meter>
      //Plans: Plans
      UnprocessableMessages: EventHubEvent<MeteringUpdateEvent> list
      LastUpdate: MessagePosition option }

    member this.metersToBeSubmitted : MarketplaceRequest seq =
        this.MeterCollection
        |> Map.toSeq
        |> Seq.collect (fun (_, meter) -> meter.UsageToBeReported)
        // |> Seq.sortBy (fun r -> r.EffectiveStartTime.ToInstant())

    static member Empty 
        with get() =
            { MeterCollection = Map.empty
              UnprocessableMessages = List.empty
              LastUpdate = None }
    
    static member Uninitialized 
        with get() : (MeterCollection option) = None

type SomeMeterCollection = MeterCollection option
 
[<Extension>]
module MeterCollection =
    let toStrM (pid) (meters: Meter seq) : string =
        meters
        |> Seq.sortBy  (fun a -> a.Subscription.MarketplaceResourceId)
        |> Seq.map (Meter.toStr pid)
        |> String.concat "\n-----------------\n"
    
    let toStr (mc: MeterCollection option) : string =
        match mc with
        | None -> ""
        | Some mc ->
            let pid =
                match mc.LastUpdate with
                | None -> ""
                | Some p -> p.PartitionID.value 
                |> sprintf "%2s"

            mc.MeterCollection
            |> Map.values
            |> toStrM pid
