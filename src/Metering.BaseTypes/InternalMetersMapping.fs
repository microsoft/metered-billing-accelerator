// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.BaseTypes

/// A mapping table, used by the aggregator, to translate an ApplicationInternalMeterName to the plan and dimension configured in Azure marketplace.
type InternalMetersMapping = 
    private | Value of Map<ApplicationInternalMeterName, DimensionId>

    member this.value 
        with get() : Map<ApplicationInternalMeterName, DimensionId> =
            let v (Value x) = x
            this |> v

    static member create x = (Value x)
