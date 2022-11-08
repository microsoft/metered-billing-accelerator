// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.BaseTypes

/// Collects all meters per internal metering event type
type CurrentMeterValues =
    private | Value of Map<DimensionId, SimpleMeterValue> 

    member this.value
        with get() =
            let v (Value x) = x
            this |> v

    static member create x = (Value x)  

    member internal this.toStrings =
        this.value
        |> Map.toSeq
        |> Seq.map (fun (k,v) -> 
            sprintf "%30s: %s" 
                (k.value)
                (v.ToString())
        )