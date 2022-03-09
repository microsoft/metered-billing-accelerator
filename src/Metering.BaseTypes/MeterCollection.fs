// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.BaseTypes

open System.Runtime.CompilerServices
open Metering.BaseTypes.EventHub

type MeterCollection = 
    { MeterCollection: Map<InternalResourceId, Meter>
      //Plans: Plans
      UnprocessableMessages: EventHubEvent<MeteringUpdateEvent> list
      LastUpdate: MessagePosition option }

type SomeMeterCollection = MeterCollection option
 
[<Extension>]
module MeterCollection =
    let value (x : MeterCollection) = x.MeterCollection
    
    let Uninitialized : (MeterCollection option) = None
    let Empty =
        { MeterCollection = Map.empty
          UnprocessableMessages = List.empty
          LastUpdate = None          
          // Plans = Map.empty
          }

    let toStrM (pid) (meters: Meter seq) : string =
        meters
        |> Seq.sortBy  (fun a -> a.Subscription.InternalResourceId)
        |> Seq.map (Meter.toStr pid)
        |> String.concat "\n-----------------\n"
    
    let toStr (mc: MeterCollection option) : string =
        match mc with
        | None -> ""
        | Some mc ->
            let pid =
                match mc.LastUpdate with
                | None -> ""
                | Some p -> p.PartitionID |> PartitionID.value 
                |> sprintf "%2s"

            mc.MeterCollection
            |> Map.values
            |> toStrM pid
    
    [<Extension>]
    let metersToBeSubmitted  (x : MeterCollection) : MarketplaceRequest seq =
        x.MeterCollection
        |> Map.toSeq
        |> Seq.collect (fun (_, meter) -> meter.UsageToBeReported)
        // |> Seq.sortBy (fun r -> r.EffectiveStartTime.ToInstant())
