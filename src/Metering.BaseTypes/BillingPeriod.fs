// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.BaseTypes

open NodaTime


type BillingPeriod = // Each time the subscription is renewed, a new billing period start
    { Start: MeteringDateTime
      End: MeteringDateTime
      Index: uint }

module BillingPeriod =
    let previousBillingIntervalCanBeClosedNewEvent (previous: MeteringDateTime) (eventTime: MeteringDateTime) : bool =
        previous.Hour <> eventTime.Hour || eventTime - previous >= Duration.FromHours(1.0)
