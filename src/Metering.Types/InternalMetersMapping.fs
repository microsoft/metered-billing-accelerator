// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.Types

/// A mapping table, used by the aggregator, to translate an ApplicationInternalMeterName to the plan and dimension configured in Azure marketplace.
type InternalMetersMapping = 
    InternalMetersMapping of Map<ApplicationInternalMeterName, DimensionId>

module InternalMetersMapping =
    let value (InternalMetersMapping x) = x
    let create x = (InternalMetersMapping x)
