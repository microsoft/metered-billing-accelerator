// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.BaseTypes

open Metering.BaseTypes.WaterfallTypes

type MeterValue =
    | SimpleMeterValue of SimpleMeterValue
    | WaterfallMeterValue of WaterfallMeterValue