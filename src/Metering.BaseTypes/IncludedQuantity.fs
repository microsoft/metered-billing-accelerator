// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.BaseTypes

type IncludedQuantity = 
    { Quantity: Quantity
      Created: MeteringDateTime 
      LastUpdate: MeteringDateTime }

module IncludedQuantity =
    let set now quantity (q: IncludedQuantity) = { q with Quantity = quantity ; LastUpdate = now }

    let decrease now quantity (q: IncludedQuantity) = { q with Quantity = q.Quantity - quantity ; LastUpdate = now }

    let toStr (iq: IncludedQuantity) : string =
        sprintf "Remaining %s" (iq.Quantity.ToString())
