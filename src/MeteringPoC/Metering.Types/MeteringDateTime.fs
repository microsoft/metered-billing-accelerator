namespace Metering.Types

type MeteringDateTime = NodaTime.ZonedDateTime

module MeteringDateTime =
    open System
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

    let blobName : (MeteringDateTime -> string) =
        ("yyyy-MM-dd--HH-mm-ss" |> toPattern).Format

    let toStr (d: MeteringDateTime) : string =
        d |> withNanoSecondsInZulu.Format
    
    let fromStr (str: string) : MeteringDateTime =      
        meteringDateTimePatterns
        |> List.map (fun p -> p.Parse(str))
        |> List.filter (fun p -> p.Success)
        |> List.map (fun p -> p.Value)
        |> List.head

    let fromDateTimeOffset (dtos: DateTimeOffset) : MeteringDateTime =
        ZonedDateTime(Instant.FromDateTimeOffset(dtos), DateTimeZone.Utc)

    let beginOfTheHour (m: MeteringDateTime) : MeteringDateTime =
        let adjuster (x: LocalTime) = new LocalTime(x.Hour,  0, 0, 0)
        MeteringDateTime(m.LocalDateTime.With(FSharpFuncUtil.Create adjuster), m.Zone, m.Offset)

    let now () : MeteringDateTime =
        ZonedDateTime(SystemClock.Instance.GetCurrentInstant(), DateTimeZone.Utc)
    
    let create year month day hour minute second = 
        new MeteringDateTime(
            localDateTime = new LocalDateTime(
                year = year, month = month, day = day, 
                hour = hour, minute = minute, second = second),
            zone = DateTimeZone.Utc,
            offset = Offset.Zero)
