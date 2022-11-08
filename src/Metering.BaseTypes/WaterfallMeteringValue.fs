
// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.BaseTypes

type WaterfallDescriptionItem = 
    { Begin: Quantity
      Name: string }

type Range =
    { DimensionId: DimensionId
      LowerIncluding: Quantity
      UpperExcluding: Quantity }

type Overage =
    { DimensionId: DimensionId
      LowerIncluding: Quantity }

type WaterfallModelRow =  
  | FreeIncluded of Quantity // For included quantities, no reporting to the metering API
  | Range of Range
  | Overage of Overage

type WaterfallDescription =
  WaterfallDescriptionItem list

type WaterfallModel = 
  WaterfallModelRow list

type WaterfallMeterValue =
  { Model: WaterfallModel 
    Total: Quantity
    Consumption: Map<DimensionId, Quantity> }