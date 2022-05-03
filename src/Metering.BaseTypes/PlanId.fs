// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.BaseTypes

/// The ID of the plan (as defined in partner center)
type PlanId = 
    { Value: string }

    static member create value = { Value = value }
    
/// The immutable dimension identifier referenced while emitting usage events (as defined in partner center).
type DimensionId =
    { Value: string }
    
    static member create value = { Value = value }
