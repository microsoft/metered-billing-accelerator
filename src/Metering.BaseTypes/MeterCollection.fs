// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.BaseTypes

open System.Runtime.CompilerServices
open Metering.BaseTypes.EventHub

type MeterCollection =
    { Meters: Meter list
      UnprocessableMessages: EventHubEvent<MeteringUpdateEvent> list
      LastUpdate: MessagePosition option }

    member this.MetersToBeSubmitted() : MarketplaceRequest seq =
        this.Meters
        |> Seq.collect (fun meter -> meter.UsageToBeReported)

    static member Empty
        with get() =
            { Meters = List.empty
              UnprocessableMessages = List.empty
              LastUpdate = None }

    static member Uninitialized
        with get() : (MeterCollection option) = None

type SomeMeterCollection = MeterCollection option

[<Extension>]
module MeterCollection =
    let find (marketplaceResourceId: MarketplaceResourceId) (state: MeterCollection) : Meter  =
        state.Meters
        |> List.find (Meter.matches marketplaceResourceId)

    let contains (marketplaceResourceId: MarketplaceResourceId) (state: MeterCollection) : bool =
        state.Meters
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

            mc.Meters
            |> toStrM pid
