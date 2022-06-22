// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.BaseTypes

type IncludedQuantity = 
    { Quantity: Quantity
      Created: MeteringDateTime 
      LastUpdate: MeteringDateTime }

    override this.ToString() = sprintf "Remaining %s" (this.Quantity.ToString())

    member this.set now quantity = { this with Quantity = quantity ; LastUpdate = now }

    member this.decrease now quantity = { this with Quantity = this.Quantity - quantity ; LastUpdate = now }
