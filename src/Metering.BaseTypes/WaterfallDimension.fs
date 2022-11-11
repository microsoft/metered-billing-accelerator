// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.BaseTypes.WaterfallTypes

open Metering.BaseTypes

/// Serialization
type WaterfallBillingDimensionItem = 
    { Threshold: Quantity
      DimensionId: DimensionId }

type WaterfallBillingDimension =
    { /// Application-internal name of the meter / billing dimension. 
      ApplicationInternalMeterName: ApplicationInternalMeterName

      /// The dimension as Marketplace knows it.
      Tiers: WaterfallBillingDimensionItem list}
