// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.BaseTypes

type ConsumedQuantity = 
    { Amount: Quantity
      Created: MeteringDateTime 
      LastUpdate: MeteringDateTime }
    
    override this.ToString() = sprintf "%s consumed"  (this.Amount.ToString())

    member this.increaseConsumption now amount = { this with Amount = this.Amount + amount ; LastUpdate = now }

    static member create now amount = { Amount = amount; Created = now ; LastUpdate = now }
