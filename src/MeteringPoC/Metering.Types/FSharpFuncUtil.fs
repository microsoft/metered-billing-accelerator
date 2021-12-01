namespace Metering.Types

open System
open System.Collections.Generic
open System.Runtime.CompilerServices

[<Extension>]
type public FSharpFuncUtil = 

    [<Extension>] 
    static member IsSome<'t> (value: 't option) : bool = value.IsSome
    
    [<Extension>] 
    static member IsNone<'t> (value: 't option) : bool = value.IsNone

    [<Extension>] 
    static member ToFSharp<'t> (source: IEnumerable<'t>) : 't list = source |> List.ofSeq

    [<Extension>] 
    static member OptionEqualsValue<'t when 't : equality> (tOption: 't option) (t: 't) : bool = 
        match tOption with
        | Some v -> v.Equals(t)
        | None -> false

    // https://blogs.msdn.microsoft.com/jaredpar/2010/07/27/converting-system-funct1-tn-to-fsharpfuncttresult/
    [<Extension>] 
    static member ConverterToFSharpFunc<'a,'b> (converter: Converter<'a,'b>) = fun x -> converter.Invoke(x)

    [<Extension>] 
    static member ToFSharpFunc<'a,'b> (func: Func<'a,'b>) = fun a -> func.Invoke(a)

    [<Extension>] 
    static member ToFSharpFunc<'a,'b,'c> (func: Func<'a,'b,'c>) = fun a b -> func.Invoke(a,b)

    [<Extension>] 
    static member ToFSharpFunc<'a,'b,'c,'d> (func: Func<'a,'b,'c,'d>) = fun a b c -> func.Invoke(a,b,c)

    [<Extension>] 
    static member ToFSharpFunc<'a,'b,'c,'d,'e> (func: Func<'a,'b,'c,'d,'e>) = fun a b c d -> func.Invoke(a,b,c,d)

    [<Extension>] 
    static member ToFSharpFunc<'a,'b,'c,'d,'e,'f> (func: Func<'a,'b,'c,'d,'e,'f>) = fun a b c d e -> func.Invoke(a,b,c,d,e)

    static member Create<'a,'b> (func: Func<'a,'b>) = FSharpFuncUtil.ToFSharpFunc func

    static member Create<'a,'b,'c> (func: Func<'a,'b,'c>) = FSharpFuncUtil.ToFSharpFunc func

    static member Create<'a,'b,'c,'d> (func: Func<'a,'b,'c,'d>) = FSharpFuncUtil.ToFSharpFunc func

    static member Create<'a,'b,'c,'d,'e> (func: Func<'a,'b,'c,'d,'e>) = FSharpFuncUtil.ToFSharpFunc func

    static member Create<'a,'b,'c,'d,'e,'f> (func: Func<'a,'b,'c,'d,'e,'f>) = FSharpFuncUtil.ToFSharpFunc func
