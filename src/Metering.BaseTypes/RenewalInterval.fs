// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.BaseTypes

open NodaTime

type RenewalInterval =
    | Monthly
    | Annually

    member this.Duration 
        with get() = 
            match this with
            | Monthly -> Period.FromMonths(1)
            | Annually -> Period.FromYears(1)
    
     member this.add (i: uint) : Period =
        match this with
        | Monthly -> Period.FromMonths(int i)
        | Annually -> Period.FromYears(int i)
