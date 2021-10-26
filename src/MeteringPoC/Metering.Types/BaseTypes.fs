namespace Metering.Types

type MeteringDateTime = NodaTime.ZonedDateTime

module MeteringDateTime =
    open NodaTime
    open NodaTime.Text

    let meteringDateTimePatterns = 
        [ 
            // this is the default for serialization
            "yyyy-MM-dd--HH-mm-ss"
            "yyyy-MM-dd--HH-mm-ss-FFF" 
        ]
        |> List.map (fun p -> ZonedDateTimePattern.CreateWithInvariantCulture(p, DateTimeZoneProviders.Bcl))

    let toStr (d: MeteringDateTime) : string = meteringDateTimePatterns.Head.Format(d)
    
    let fromStr (str: string) : MeteringDateTime = 
        meteringDateTimePatterns
        |> List.map (fun p -> p.Parse(str))
        |> List.filter (fun p -> p.Success)
        |> List.map (fun p -> p.Value)
        |> List.head
