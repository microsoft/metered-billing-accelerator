// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.Types

type PlanId = private PlanId of string

module PlanId = 
    let value (PlanId x) = x
    let create x = (PlanId x)

/// The immutable dimension identifier referenced while emitting usage events.
type DimensionId = private DimensionId of string

module DimensionId = 
    let value (DimensionId x) = x
    let create x = (DimensionId x)

/// The description of the billing unit, for example "per text message" or "per 100 emails".
type UnitOfMeasure = private UnitOfMeasure of string

module UnitOfMeasure = 
    let value (UnitOfMeasure x) = x
    let create x = (UnitOfMeasure x)
