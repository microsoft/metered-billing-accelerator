// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.BaseTypes

type Plan =
    { PlanId: PlanId
      BillingDimensions: BillingDimensions }

module Plan =
    let updateBillingDimensions dimensions plan = { plan with BillingDimensions = dimensions }
