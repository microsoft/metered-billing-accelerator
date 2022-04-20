// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.BaseTypes

/// A meter name used between app and aggregator
type ApplicationInternalMeterName = private ApplicationInternalMeterName of string 

module ApplicationInternalMeterName =
    let value (ApplicationInternalMeterName x) = x
    let create x = (ApplicationInternalMeterName x)
