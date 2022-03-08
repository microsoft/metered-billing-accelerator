// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.Types

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
            | (Infinite, _) -> Infinite
            | (_, Infinite) -> failwith "This must never happen"
            |> function
                | MeteringInt i -> MeteringInt i
                | MeteringFloat f -> 
                    if f < 0.0
                    then failwith "Cannot be negative"
                    else MeteringFloat f
                | Infinite -> Infinite

module Quantity =    
    [<CompiledName("create")>]
    let createInt i = (MeteringInt i)
    
    [<CompiledName("create")>]
    let createFloat f = (MeteringFloat f)
    
    let fromString (s: string) =
        if s = "Infinite"
        then Infinite
        else 
            if s.Contains(".")
            then s |> Double.Parse |> createFloat
            else s |> UInt32.Parse |> createInt

    [<CompiledName("some")>]
    let someInt = createInt >> Some
    
    [<CompiledName("some")>]
    let someFloat = createFloat >> Some
    
    let none : (Quantity option) = None

    let valueAsInt = function
        | MeteringInt i -> i
        | MeteringFloat f -> uint32 f
        | Infinite -> failwith "Trying to convert Infinity to an uint64"

    let valueAsFloat = function
        | MeteringInt i -> float i
        | MeteringFloat f -> f
        | Infinite -> failwith "Trying to convert Infinity to a float"

    let toStr : (Quantity -> string) = 
        function
        | MeteringInt i -> i.ToString()
        | MeteringFloat f -> f.ToString()
        | Infinite -> "Infinite"
        
    let isAllowedIncomingQuantity : (Quantity -> bool) =
        function
        | MeteringInt i -> true
        | MeteringFloat f -> f >= 0.0
        | Infinite -> false
