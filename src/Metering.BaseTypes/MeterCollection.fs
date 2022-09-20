// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.BaseTypes

open System.Runtime.CompilerServices
open Metering.BaseTypes.EventHub

type MeterCollection = 
    { MeterCollection: Meter list
      UnprocessableMessages: EventHubEvent<MeteringUpdateEvent> list
      LastUpdate: MessagePosition option }

    member this.metersToBeSubmitted : MarketplaceRequest seq =
        this.MeterCollection
        |> Seq.collect (fun meter -> meter.UsageToBeReported)

    static member Empty 
        with get() =
            { MeterCollection = List.empty
              UnprocessableMessages = List.empty
              LastUpdate = None }
    
    static member Uninitialized 
        with get() : (MeterCollection option) = None

type SomeMeterCollection = MeterCollection option
 
[<Extension>]
module MeterCollection =
    let find (marketplaceResourceId: MarketplaceResourceId) (state: MeterCollection) : Meter  =
        state.MeterCollection
        |> List.find (Meter.matches marketplaceResourceId)
    
    let contains (marketplaceResourceId: MarketplaceResourceId) (state: MeterCollection) : bool =
        state.MeterCollection
        |> List.exists (Meter.matches marketplaceResourceId)

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
            |> toStrM pid
