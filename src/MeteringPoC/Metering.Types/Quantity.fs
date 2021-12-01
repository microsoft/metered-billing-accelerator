namespace Metering.Types

type Quantity =
    /// Indicates that this plan participates in this dimension, but does not emit usage against this dimension. 
    | Infinite // https://docs.microsoft.com/en-us/azure/marketplace/partner-center-portal/saas-metered-billing
    | MeteringInt of uint64
    | MeteringFloat of decimal

    static member (+) (a: Quantity, b: Quantity) =
        match (a, b) with
            | ((MeteringInt a), (MeteringInt b)) -> MeteringInt  (a + b)
            | ((MeteringInt a), (MeteringFloat b)) -> MeteringFloat (decimal a + b)
            | ((MeteringFloat a), (MeteringInt b)) -> MeteringFloat (a + decimal b)
            | ((MeteringFloat a), (MeteringFloat b)) -> MeteringFloat (a + b)
            | (Infinite, _) -> Infinite
            | (_, Infinite) -> Infinite

    static member (-) (a: Quantity, b: Quantity) =
        match (a, b) with
            | ((MeteringInt a), (MeteringInt b)) -> MeteringInt (a - b)
            | ((MeteringInt a), (MeteringFloat b)) -> MeteringFloat (decimal a - b)
            | ((MeteringFloat a), (MeteringInt b)) -> MeteringFloat (a - decimal b)
            | ((MeteringFloat a), (MeteringFloat b)) -> MeteringFloat (a - b)
            | (Infinite, _) -> Infinite
            | (_, Infinite) -> failwith "This must never happen"
        
module Quantity =
    let createInt i = (MeteringInt i)
    let createFloat f = (MeteringFloat f)

    let someInt = createInt >> Some
    let someFloat = createFloat >> Some
    let none : (Quantity option) = None

    let valueAsInt = function
        | MeteringInt i -> i
        | MeteringFloat f -> uint64 f
        | Infinite -> failwith "Trying to convert Infinity to an uint64"

    let valueAsFloat = function
        | MeteringInt i -> decimal i
        | MeteringFloat f -> f
        | Infinite -> failwith "Trying to convert Infinity to a decimal"
