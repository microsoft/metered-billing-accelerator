// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.BaseTypes

open NodaTime

type RenewalInterval =
    | Monthly
    | Annually
    | TwoYears
    | ThreeYears

    member this.Duration
        with get() =
            match this with
            | Monthly -> Period.FromMonths(1)
            | Annually -> Period.FromYears(1)
            | TwoYears -> Period.FromYears(2)
            | ThreeYears -> Period.FromYears(3)

     member this.add (i: uint) : Period =
        match this with
        | Monthly -> Period.FromMonths(int i)
        | Annually -> Period.FromYears(int i)
        | TwoYears -> Period.FromYears(2 * (int i))
        | ThreeYears -> Period.FromYears(3 * (int i))
