namespace Metering.Types

open Metering.Types.EventHub
open System.Runtime.CompilerServices

type MeterCollection = 
    { MeterCollection: Map<InternalResourceId, Meter>
      LastUpdate: MessagePosition option }

type SomeMeterCollection = MeterCollection option
 
[<Extension>]
module MeterCollection =
    let value (x : MeterCollection) = x.MeterCollection
    let create lu x = { MeterCollection = x; LastUpdate = Some lu }
    
    let Uninitialized : (MeterCollection option) = None
    let Empty = { MeterCollection = Map.empty; LastUpdate = None }
