// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.BaseTypes

/// A meter name used between app and aggregator
type ApplicationInternalMeterName = 
    private | Value of string 

    member this.value
        with get() =
            let v (Value x) = x
            this |> v

    static member create x = (Value x)
