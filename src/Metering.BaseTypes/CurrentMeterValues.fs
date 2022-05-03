// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.BaseTypes

type CurrentMeterValues = // Collects all meters per internal metering event type
    private | CurrentMeterValues of Map<DimensionId, MeterValue> 

    member this.value
        with get() =
            let v (CurrentMeterValues x) = x
            this |> v

    static member create x = (CurrentMeterValues x)  

    member internal this.toStrings =
        this.value
        |> Map.toSeq
        |> Seq.map (fun (k,v) -> 
            sprintf "%30s: %s" 
                (k.value)
                (v.ToString())
        )