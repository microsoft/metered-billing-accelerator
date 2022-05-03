// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.BaseTypes

/// A meter name used between app and aggregator
type ApplicationInternalMeterName = 
    private | ApplicationInternalMeterName of string 

    member this.value
        with get() =
            let v (ApplicationInternalMeterName x) = x
            this |> v
    
    static member create x = (ApplicationInternalMeterName x)
