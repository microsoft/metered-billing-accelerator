// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.BaseTypes

type ConsumedQuantity = 
    { Amount: Quantity
      Created: MeteringDateTime 
      LastUpdate: MeteringDateTime }

module ConsumedQuantity =
    let create now amount = { Amount = amount; Created = now ; LastUpdate = now }
    let increaseConsumption now amount q = { q with Amount = q.Amount + amount ; LastUpdate = now }

    let toStr (cq: ConsumedQuantity) : string =
        cq.Amount |> Quantity.toStr |> sprintf "%s consumed" 
        