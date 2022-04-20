// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.BaseTypes

/// The ID of the plan (as defined in partner center)
type PlanId = private PlanId of string

module PlanId = 
    let value (PlanId x) = x
    let create x = (PlanId x)

/// The immutable dimension identifier referenced while emitting usage events (as defined in partner center).
type DimensionId = private DimensionId of string

module DimensionId = 
    let value (DimensionId x) = x
    let create x = (DimensionId x)
