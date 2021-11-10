namespace Metering.Types

type MeteringDateTime = NodaTime.ZonedDateTime

module MeteringDateTime =
    open NodaTime
    open NodaTime.Text

    let private toPattern p = 
        ZonedDateTimePattern.CreateWithInvariantCulture(p, DateTimeZoneProviders.Bcl)

    let onlySecond = "yyyy-MM-ddTHH:mm:ss" |> toPattern
    let onlySecondZulu = "yyyy-MM-ddTHH:mm:ss'Z'" |> toPattern
    let withNanoSecondsInZulu = "yyyy-MM-ddTHH:mm:ss.FFFFFFF'Z'" |> toPattern
    let meteringDateTimePatterns = 
        [ 
            withNanoSecondsInZulu // "2021-11-05T10:00:25.7798568Z",
            onlySecondZulu
            onlySecond 
        ]
        
    let toStr (d: MeteringDateTime) : string =
        d |> withNanoSecondsInZulu.Format
    
    let fromStr (str: string) : MeteringDateTime =      
        meteringDateTimePatterns
        |> List.map (fun p -> p.Parse(str))
        |> List.filter (fun p -> p.Success)
        |> List.map (fun p -> p.Value)
        |> List.head

    let beginOfTheHour (m: MeteringDateTime) : MeteringDateTime =
        let adjuster (x: LocalTime) = new LocalTime(x.Hour,  0, 0, 0)
        MeteringDateTime(m.LocalDateTime.With(FSharpFuncUtil.Create adjuster), m.Zone, m.Offset)

    let now () : MeteringDateTime =
        ZonedDateTime(SystemClock.Instance.GetCurrentInstant(), DateTimeZone.Utc)
