// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.Types

type InternalUsageEvent = // From app to aggregator
    { InternalResourceId: InternalResourceId
      Timestamp: MeteringDateTime  // timestamp from the sending app
      MeterName: ApplicationInternalMeterName
      Quantity: Quantity
      Properties: Map<string, string> option}

module InternalUsageEvent =
    let toStr (x: InternalUsageEvent) : string =
        $"{x.Timestamp |> MeteringDateTime.toStr}: InternalUsageEvent {x.InternalResourceId |> InternalResourceId.toStr} {x.MeterName |> ApplicationInternalMeterName.value}={x.Quantity |> Quantity.valueAsFloat}"