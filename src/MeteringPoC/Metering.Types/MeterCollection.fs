namespace Metering.Types

open Metering.Types.EventHub
open System.Runtime.CompilerServices

type MeterCollection = 
    { MeterCollection: Map<InternalResourceId, Meter>
      UnprocessableMessages: EventHubEvent<MeteringUpdateEvent> list
      LastUpdate: MessagePosition option }

type SomeMeterCollection = MeterCollection option
 
[<Extension>]
module MeterCollection =
    let value (x : MeterCollection) = x.MeterCollection
    
    let Uninitialized : (MeterCollection option) = None
    let Empty = { MeterCollection = Map.empty; LastUpdate = None; UnprocessableMessages = [] }

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
