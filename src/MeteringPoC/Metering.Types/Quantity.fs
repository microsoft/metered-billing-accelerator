namespace Metering.Types

open System

[<CustomComparison; CustomEquality>]
type Quantity =
    /// Indicates that this plan participates in this dimension, but does not emit usage against this dimension. 
    | Infinite // https://docs.microsoft.com/en-us/azure/marketplace/partner-center-portal/saas-metered-billing
    | MeteringInt of uint64
    | MeteringFloat of decimal
    interface IComparable<Quantity> with
        member this.CompareTo other : int =
            match (this, other) with
            | ((MeteringInt t), (MeteringInt o)) -> t.CompareTo(o)
            | ((MeteringInt t), (MeteringFloat o)) -> (decimal t).CompareTo(o)
            | ((MeteringFloat t), (MeteringInt o)) -> t.CompareTo(decimal o)
            | ((MeteringFloat t), (MeteringFloat o)) -> t.CompareTo(o)
            | (Infinite, _) -> 1
            | (_, Infinite) -> -1

    interface IComparable with
        member this.CompareTo obj =
            match obj with
              | null                 -> 1
              | :? Quantity as other -> (this :> IComparable<_>).CompareTo other
              | _                    -> invalidArg "obj" $"not a {nameof(Quantity)}"
        
    interface IEquatable<Quantity> with
        member this.Equals other =
            match (this, other) with
            | ((MeteringInt t), (MeteringInt o)) -> t = o
            | ((MeteringInt t), (MeteringFloat o)) -> (decimal t).Equals(o)
            | ((MeteringFloat t), (MeteringInt o)) -> t.Equals(decimal o)
            | ((MeteringFloat t), (MeteringFloat o)) -> t = o
            | (Infinite, Infinite) -> true
            | (_, _) -> false

    override this.Equals obj =
        match obj with
          | :? Quantity as other -> (this :> IEquatable<_>).Equals other
          | _                    -> false

    override this.GetHashCode () =
        match this with
        | Infinite -> "Infinite".GetHashCode()
        | MeteringInt i -> i.GetHashCode()
        | MeteringFloat f -> f.GetHashCode()


    static member (+) (a: Quantity, b: Quantity) =
        match (a, b) with
            | ((MeteringInt a), (MeteringInt b)) -> MeteringInt  (a + b)
            | ((MeteringInt a), (MeteringFloat b)) -> MeteringFloat (decimal a + b)
            | ((MeteringFloat a), (MeteringInt b)) -> MeteringFloat(a + decimal b)
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
            |> function
                | MeteringInt i -> MeteringInt i
                | MeteringFloat f -> 
                    if f < 0M
                    then failwith "Cannot be negative"
                    else MeteringFloat f
                | Infinite -> Infinite

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

    let toStr (q: Quantity) : string = 
        q 
        |> function
            | MeteringInt i -> i.ToString()
            | MeteringFloat f -> f.ToString()
            | Infinite -> "Infinite"
        