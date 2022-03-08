namespace Metering.Types

open System
open System.Reactive.Linq
open System.Runtime.CompilerServices

[<Extension>]
type public ObservableExtension = 

    /// Checks all items in the observable, and returns x for all which are Some(x).
    [<Extension>]    
    static member Choose<'TSource> (o: IObservable<'TSource option>) : IObservable<'TSource> = 
        // this is equivalent to the C# of 
        //
        // observable
        //     .Where(x => x.IsSome())
        //     .Select(x => x.Value)
        Observable.Select(
            source = Observable.Where(
                source = o, 
                predicate = ((fun v -> v.IsSome) : ('TSource option -> bool))),
            selector = ((fun v -> v.Value):('TSource option -> 'TSource)))

