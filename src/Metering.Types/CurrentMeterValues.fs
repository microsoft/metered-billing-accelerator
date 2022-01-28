// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.Types

type CurrentMeterValues = // Collects all meters per internal metering event type
    Map<DimensionId, MeterValue> 

module CurrentMeterValues =
    let toStr (cmv: CurrentMeterValues) =
        cmv
        |> Map.toSeq
        |> Seq.map (fun (k,v) -> 
            sprintf "%30s: %s" 
                (k |> DimensionId.value)
                (v |> MeterValue.toStr)
        )

