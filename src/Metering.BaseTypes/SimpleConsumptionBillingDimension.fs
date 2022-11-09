// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.BaseTypes

/// The 'simple consumption' represents the most simple Azure Marketplace metering model. 
/// There is a dimension with included quantities, and once the included quantities are consumed, the overage starts counting.
type SimpleConsumptionBillingDimension = 
    { /// Application-internal name of the meter / billing dimension. 
      InternalName: ApplicationInternalMeterName

      /// The dimension as Marketplace knows it.
      DimensionId: DimensionId

      /// The dimensions configured
      IncludedQuantity: Quantity }
