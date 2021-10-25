open System
open Metering.Types
open System.Globalization
open NodaTime
open Metering
open Metering.Types.EventHub
open Thoth.Json.Net


let parseDate (p: string) =
    DateTime.ParseExact(p, [|"yyyy-MM-dd--HH-mm-ss"|], CultureInfo.CurrentCulture, DateTimeStyles.AssumeUniversal)

let parseUsageEvents events =
    let parseUsageEvent (s: string) =
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

let jsonDecode<'T> json = 
    match Decode.Auto.fromString<'T>(json, extra = myExtraCoders) with
    | Ok r -> r
    | Result.Error e -> failwith e

let jsonEncode o = Encode.Auto.toString (1, o, extra = myExtraCoders)

let parseSubscriptionCreation date str = 
    str
    |> jsonDecode<SubscriptionCreationInformation>
    |> SubscriptionPurchased
    |> (fun e -> { MeteringUpdateEvent = e; MessagePosition = { 
         PartitionID = "1"
         SequenceNumber = 1UL
         PartitionTimestamp = date |> parseDate
    }})

let parseConsumptionEvents (str: string) = 
    str
    |> (fun s -> s.Split([|"\n"|], StringSplitOptions.RemoveEmptyEntries))
    |> Array.toList
    |> parseUsageEvents

[<EntryPoint>]
let main argv = 
    let dateTimeToNoda (dateTime : DateTime) =
        ZonedDateTime(LocalDateTime.FromDateTime(dateTime.ToUniversalTime()), DateTimeZone.Utc, Offset.Zero)

    let subscriptionCreationEvent =
         """
    {
        "plans": [
            { "planId": "plan1", "billingDimensions": [
                { "dimension": "nodecharge",       "name": "Per Node Connected",         "unitOfMeasure": "node/hour", "includedQuantity": {} },
                { "dimension": "cpucharge",        "name": "Per CPU urage",              "unitOfMeasure": "cpu/hour", "includedQuantity": {} },
                { "dimension": "datasourcecharge", "name": "Per DataSource Integration", "unitOfMeasure": "ds/hour", "includedQuantity": {} },
                { "dimension": "messagecharge",    "name": "Per Message Transmitted",    "unitOfMeasure": "message/hour", "includedQuantity": {} } ] },
            { "planId": "plan2", "billingDimensions": [
                { "dimension": "MachineLearningJob", "name": "An expensive machine learning job", "unitOfMeasure": "machine learning jobs", "includedQuantity": { "monthly": "10" } },
                { "dimension": "EMailCampaign",      "name": "An e-mail sent for campaign usage", "unitOfMeasure": "e-mails", "includedQuantity": { "monthly": "250000" } } ] } ],
        "metersMapping": {
            "email": { "plan": "plan2", "dimension": "EMailCampaign" },
            "ml":    { "plan": "plan2", "dimension": "MachineLearningJob" } },
        "initialPurchase": { "plan": "plan2", "renewalInterval": "Monthly", "subscriptionStart": "2021-10-01" }
    }""" |> parseSubscriptionCreation "2021-10-01--13-10-55"

    // Position read pointer in EventHub to 001002, and start applying 
    let consumptionEvents = 
        """
        001002 | 2021-10-13--14-12-02 | ml    |   1 | Department=Data Science, Project ID=Skunkworks vNext
        001003 | 2021-10-13--15-12-03 | ml    |   2
        001004 | 2021-10-13--15-13-02 | email | 300 | Email Campaign=User retention, Department=Marketing
        001005 | 2021-10-13--15-12-08 | ml    |  20
        """ |> parseConsumptionEvents
        
    let eventsFromEventHub = subscriptionCreationEvent :: consumptionEvents // The first event must be the subscription creation, followed by many consumption events

    let emptyBalance : MeteringState option = None // We start completely uninitialized

    let newBalance =
        eventsFromEventHub
        |> Logic.handleEvents emptyBalance
        |> jsonEncode
        |> jsonDecode<MeteringState>

    printfn "newBalance %A" newBalance
    0
