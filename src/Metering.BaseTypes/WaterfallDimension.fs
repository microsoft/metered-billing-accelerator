// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.BaseTypes.WaterfallTypes

open Metering.BaseTypes

/// Serialization
type WaterfallDescriptionItem = 
    { Threshold: Quantity
      DimensionId: DimensionId }

type WaterfallBillingDimension =
    { /// Application-internal name of the meter / billing dimension. 
      InternalName: ApplicationInternalMeterName

      /// The dimension as Marketplace knows it.
      Tiers: WaterfallDescriptionItem list}
