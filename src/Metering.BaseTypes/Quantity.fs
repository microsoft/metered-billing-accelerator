// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.BaseTypes

open System

[<CustomComparison; CustomEquality>]
type Quantity =
    /// Indicates that this plan participates in this dimension, but does not emit usage against this dimension. 
    | Infinite // https://docs.microsoft.com/en-us/azure/marketplace/partner-center-portal/saas-metered-billing
    | MeteringInt of uint
    | MeteringFloat of float

    interface IComparable<Quantity> with
        member this.CompareTo other : int =
            match (this, other) with
            | ((MeteringInt t), (MeteringInt o)) -> t.CompareTo(o)
            | ((MeteringInt t), (MeteringFloat o)) -> (float t).CompareTo(o)
            | ((MeteringFloat t), (MeteringInt o)) -> t.CompareTo(float o)
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
            | ((MeteringInt t), (MeteringFloat o)) -> (float t).Equals(o)
            | ((MeteringFloat t), (MeteringInt o)) -> t.Equals(float o)
            | ((MeteringFloat t), (MeteringFloat o)) -> t = o
            | (Infinite, Infinite) -> true
            | (_, _) -> false

    override this.ToString() =
        match this with
        | MeteringInt i -> i.ToString()
        | MeteringFloat f -> f.ToString()
        | Infinite -> "Infinite"

    override this.Equals obj =
        match obj with
          | :? Quantity as other -> (this :> IEquatable<_>).Equals other
          | _                    -> false

    override this.GetHashCode () =
        match this with
        | Infinite -> "Infinite".GetHashCode()
        | MeteringInt i -> i.GetHashCode()
        | MeteringFloat f -> f.GetHashCode()

    member this.AsInt = 
        match this with
        | MeteringInt i -> i
        | MeteringFloat f -> uint32 f
        | Infinite -> failwith $"Trying to convert {nameof(Infinite)} to an uint64"

    member this.AsFloat = 
        match this with
        | MeteringInt i -> float i
        | MeteringFloat f -> f
        | Infinite -> failwith $"Trying to convert {nameof(Infinite)} to a float"
        
    member this.isAllowedIncomingQuantity =
        match this with
        | MeteringInt i -> true
        | MeteringFloat f -> f >= 0.0
        | Infinite -> false

    static member fromString (s: string) =
        if s = "Infinite"
        then Infinite
        else 
            if s.Contains(".")
            then s |> Double.Parse |> MeteringFloat
            else s |> UInt32.Parse |> MeteringInt
    
    static member Zero
        with get() = (MeteringInt 0u)

    static member None 
        with get() : (Quantity option) = None

    static member create (i: uint) = (MeteringInt i)

    static member create (f: float) = (MeteringFloat f)
    
    [<CompiledName("some")>]
    static member someInt = MeteringInt >> Some

    [<CompiledName("some")>]
    static member someFloat = MeteringFloat >> Some

    static member (+) (a: Quantity, b: Quantity) =
        match (a, b) with
            | ((MeteringInt a), (MeteringInt b)) -> MeteringInt  (a + b)
            | ((MeteringInt a), (MeteringFloat b)) -> MeteringFloat (float a + b)
            | ((MeteringFloat a), (MeteringInt b)) -> MeteringFloat(a + float b)
            | ((MeteringFloat a), (MeteringFloat b)) -> MeteringFloat (a + b)
            | (Infinite, _) -> Infinite
            | (_, Infinite) -> Infinite

    static member (-) (a: Quantity, b: Quantity) =        
        match (a, b) with
            | ((MeteringInt a), (MeteringInt b)) -> MeteringInt (a - b)
            | ((MeteringInt a), (MeteringFloat b)) -> MeteringFloat (float a - b)
            | ((MeteringFloat a), (MeteringInt b)) -> MeteringFloat (a - float b)
            | ((MeteringFloat a), (MeteringFloat b)) -> MeteringFloat (a - b)
            | (_, Infinite) -> failwith "This must never happen"
            | (Infinite, _) -> Infinite
            |> function
                | MeteringInt i -> MeteringInt i
                | MeteringFloat f -> 
                    if f < 0.0
                    then failwith "Cannot be negative"
                    else MeteringFloat f
                | Infinite -> Infinite
