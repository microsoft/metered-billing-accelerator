// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.Types

type IncludedQuantity = 
    { Monthly: Quantity option
      Annually: Quantity option
      Created: MeteringDateTime 
      LastUpdate: MeteringDateTime }

module IncludedQuantity =
    let private decrease (v: Quantity) (q: Quantity option) : Quantity option =
        match q with
        | Some a -> Some (a - v)
        | None -> failwith "mustnot"

    let setAnnually now amount q = { q with Annually = Some amount ; LastUpdate = now }
    let setMonthly now amount q = { q with Monthly = Some amount ; LastUpdate = now }
    let decreaseAnnually now amount q = { q with Annually = q.Annually |> decrease amount ; LastUpdate = now }
    let decreaseMonthly now amount q = { q with Monthly = q.Monthly |> decrease amount ; LastUpdate = now }
    let removeAnnually now q = { q with Annually = None ; LastUpdate = now }
    let removeMonthly now q = { q with Monthly = None ; LastUpdate = now }

    let toStr (iq: IncludedQuantity) : string =
        match iq.Monthly, iq.Annually with
        | None, None -> sprintf "Nothing remaining"
        | Some m, None -> sprintf "Remaining %s (month)" (m |> Quantity.toStr)
        | None, Some a -> sprintf "Remaining %s (year)" (a |> Quantity.toStr)
        | Some m, Some a -> sprintf "Remaining %s/%s (month/year)" (m |> Quantity.toStr) (a |> Quantity.toStr)
