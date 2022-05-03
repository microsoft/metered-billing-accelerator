// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.BaseTypes

/// The ID of the plan (as defined in partner center)
type PlanId = 
    private | PlanId of string

    member this.value
        with get() : string =
            let v (PlanId x) = x
            this |> v

    static member create (x: string) : PlanId = (PlanId x)
    
/// The immutable dimension identifier referenced while emitting usage events (as defined in partner center).
type DimensionId =
    private | DimensionId of string

    member this.value
        with get() : string =
            let v (DimensionId x) = x
            this |> v
    
    static member create (x: string) : DimensionId = (DimensionId x)
