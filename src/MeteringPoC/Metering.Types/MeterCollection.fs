namespace Metering.Types

open Azure.Messaging.EventHubs.Consumer
open Metering.Types.EventHub
open System.Runtime.CompilerServices

type MeterCollection = MeterCollection of Map<InternalResourceId, Meter>

type SomeMeterCollection = MeterCollection option
 
[<Extension>]
module MeterCollection =
    let value (MeterCollection x) = x
    let create x = (MeterCollection x)

    let empty : MeterCollection = Map.empty |> create
    
    let Uninitialized : (SomeMeterCollection) = None
