namespace Metering.Types

type ApplicationInternalMeterName = private ApplicationInternalMeterName of string // A meter name used between app and aggregator

module ApplicationInternalMeterName =
    let value (ApplicationInternalMeterName x) = x
    let create x = (ApplicationInternalMeterName x)
