// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.BaseTypes

/// The ID of the plan (as defined in partner center)
type PlanId = 
    private | Value of string

    member this.value
        with get() =
            let v (Value x) = x
            this |> v

    static member create x = (Value x)
    
/// The immutable dimension identifier referenced while emitting usage events (as defined in partner center).
type DimensionId =
    private | Value of string

    member this.value
        with get() =
            let v (Value x) = x
            this |> v

    static member create x = (Value x)
