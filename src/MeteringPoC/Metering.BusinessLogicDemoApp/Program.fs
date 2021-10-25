open System
open Metering.Types
open System.Globalization
open NodaTime
open Metering
open Metering.Types.MarketPlaceAPI
open Metering.Types.EventHub
open Thoth.Json.Net


// Plan
// - Containing information about how many widgets are included per month
//
// Purchase
// - Containing information about Billing dates
//
// Current consumption counter
// - Countdown for "included widgets" in current billing period
// - Once "included widgets" for current billing period are consumed, start counting hourly


let parseUsageEvents events =
    let parseUsageEvent (s: string) =
        let parseDate (p: string) =
            DateTime.ParseExact(p, [|"yyyy-MM-dd--HH-mm-ss"|], CultureInfo.CurrentCulture, DateTimeStyles.AssumeUniversal)

        let parseProps (p: string) =
            p.Split([|','|])
            |> Array.toList
            |> List.map (fun x -> x.Split([|'='|]))
            |> List.map Array.toList
            |> List.filter (fun l -> l.Length = 2)
            |> List.map (function 
                | [k;v] -> (k.Trim(), v.Trim())
                | _ -> failwith "cannot happen")
            |> Map.ofList
            |> Some

        s.Split([|'|'|], 5)
        |> Array.toList
        |> List.map (fun s -> s.Trim())
        |> function
            | [sequencenr; datestr; name; amountstr; props] -> 
                Some {
                    MeteringUpdateEvent = UsageReported {
                        Timestamp = datestr |> parseDate
                        MeterName = name
                        Quantity = amountstr |> UInt64.Parse
                        Properties = props |> parseProps }
                    MessagePosition = {
                        PartitionID = "1"
                        SequenceNumber = sequencenr |> UInt64.Parse
                        PartitionTimestamp = datestr |> parseDate }
                }
            | [sequencenr; datestr; name; amountstr] -> 
                Some {
                    MeteringUpdateEvent = UsageReported {
                        Timestamp = datestr |> parseDate
                        MeterName = name
                        Quantity = amountstr |> UInt64.Parse
                        Properties = None }
                    MessagePosition = {
                        PartitionID = "1"
                        SequenceNumber = sequencenr |> UInt64.Parse
                        PartitionTimestamp = datestr |> parseDate }
                }
            | _ -> None
    events
    |> List.map parseUsageEvent
    |> List.choose id

let myExtraCoders = Extra.empty |> Json.enrich

let fromJson<'T> json = 
    match Decode.Auto.fromString<'T>(json, extra = myExtraCoders) with
    | Ok r -> r
    | Result.Error e -> failwith e

[<EntryPoint>]
let main argv =
 
    let plans = """
            [ { "planId": "plan1", "billingDimensions": [
                { "dimension": "nodecharge",       "name": "Per Node Connected",         "unitOfMeasure": "node/hour", "includedQuantity": {} },
                { "dimension": "cpucharge",        "name": "Per CPU urage",              "unitOfMeasure": "cpu/hour", "includedQuantity": {} },
                { "dimension": "datasourcecharge", "name": "Per DataSource Integration", "unitOfMeasure": "ds/hour", "includedQuantity": {} },
                { "dimension": "messagecharge",    "name": "Per Message Transmitted",    "unitOfMeasure": "message/hour", "includedQuantity": {} } ] },
            { "planId": "plan2", "billingDimensions": [
               { "dimension": "MachineLearningJob", "name": "An expensive machine learning job", "unitOfMeasure": "machine learning jobs", "includedQuantity": { "monthly": "10" } },
               { "dimension": "EMailCampaign",      "name": "An e-mail sent for campaign usage", "unitOfMeasure": "e-mails", "includedQuantity": { "monthly": "250000" } } ] } ]
            """ |> fromJson<Plan list>


    let dateTimeToNoda (dateTime : DateTime) =
        ZonedDateTime(LocalDateTime.FromDateTime(dateTime.ToUniversalTime()), DateTimeZone.Utc, Offset.Zero)

    let oldBalance  = {
        Plans = plans
        InternalMetersMapping = 
            [
                ("email", { PlanId = "plan2"; DimensionId = "EMailCampaign" })
                ("ml", { PlanId = "plan2"; DimensionId = "MachineLearningJob" })
            ] |> Map.ofList
        InitialPurchase = {
            PlanId = "plan2"
            SubscriptionStart = LocalDate(2021, 10, 01)
            RenewalInterval = Monthly }
        // LastProcessedEventSequenceID = 237492749,
        CurrentMeterValues =
            [
                ("email", ConsumedQuantity { Amount = 100UL })
                ("ml", IncludedQuantity { Annually = None ; Monthly = Some 10UL })
            ] |> Map.ofList
        UsageToBeReported = List.empty // HTTP Call payload which still needs to be sent to MeteringAPI
        LastProcessedMessage = { 
            PartitionID = "0"
            SequenceNumber =  9UL
            PartitionTimestamp = DateTime.UtcNow.Subtract(TimeSpan.FromDays(1.0)) // |> dateTimeToNoda 
        }
    }

    let json = Encode.Auto.toString (1, oldBalance, extra = myExtraCoders)
    let f2 = Decode.Auto.fromString<MeteringState>(json, extra = myExtraCoders)
    printfn "%s \n%A" json f2

    // Position read pointer in EventHub to 237492750, and start applying 
    let eventsFromEventHub = 
        [
            "001002 | 2021-10-13--14-12-02 | ml    |   1 | Department=Data Science, Project ID=Skunkworks vNext"
            "001003 | 2021-10-13--15-12-03 | ml    |   2                                                       "
            "001004 | 2021-10-13--15-13-02 | email | 300 | Email Campaign=User retention, Department=Marketing "
            "001005 | 2021-10-13--15-12-08 | ml    |  20                                                       "
        ]
        |> parseUsageEvents

    printfn "usage %s" (Encode.Auto.toString (1, eventsFromEventHub, extra = myExtraCoders))

    let newBalance =
        eventsFromEventHub
        |> Logic.handleEvents (Some oldBalance)

    printfn "newBalance %A" newBalance
    printfn "usageEvents %A" (Encode.Auto.toString (1, eventsFromEventHub, extra = myExtraCoders))
    //printfn "oldBalance %A" oldBalance.CurrentMeterValues
    //printfn "newBalance %A" (Newtonsoft.Json.JsonConvert.SerializeObject(newBalance))
    0
