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

let parsePlans planStrings =
    let parseBillingDimension (s: string) : PlanId * BillingDimension =
        let parseQuantity(p: string) : IncludedQuantity =
            let pq (n: string) : Quantity option = 
                match UInt64.TryParse(n) with
                | (true, x) when x > 0UL -> Some x
                | _ -> None

            match (s.Split([|'/'|]) |> Array.toList |> List.map (fun s -> s.Trim())) with
            | [a; m] -> { Annually = pq(a); Monthly = pq(m) }
            | _ ->  { Annually = None; Monthly = None }

        s.Split([|'|'|], 5)
        |> Array.toList
        |> List.map (fun s -> s.Trim())
        |> function
            | [planId; dimensionId; name; unitOfMeasure; includedQuantity] -> 
                (planId, {
                    DimensionId = dimensionId
                    DimensionName = name
                    UnitOfMeasure = unitOfMeasure
                    IncludedQuantity = includedQuantity |> parseQuantity 
                })
            | [planId; dimensionId; name; unitOfMeasure] -> 
                (planId, {
                    DimensionId = dimensionId
                    DimensionName = name
                    UnitOfMeasure = unitOfMeasure
                    IncludedQuantity = { Annually = None; Monthly = None }
                })
            | _ -> failwith "parsing error"

    planStrings
    |> Seq.map parseBillingDimension
    |> Seq.groupBy(fun (plan, _) -> plan)
    |> Seq.map(fun (plan, elems) -> (plan, (elems |> Seq.map(fun (p, e) -> e) ) ))
    |> Seq.map(fun (planid, billingDims) -> { PlanId = planid; BillingDimensions = billingDims })

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

        s.Split([|'|'|], 4)
        |> Array.toList
        |> List.map (fun s -> s.Trim())
        |> function
            | [datestr; name; amountstr; props] -> 
                Some {
                    Timestamp = datestr |> parseDate
                    MeterName = name
                    Quantity = amountstr |> UInt64.Parse
                    Properties = props |> parseProps
                }
            | [datestr; name; amountstr] -> 
                Some {
                    Timestamp = datestr |> parseDate
                    MeterName = name
                    Quantity = amountstr |> UInt64.Parse
                    Properties = None
                }
            | _ -> None
    events
    |> List.map parseUsageEvent
    |> List.choose id

[<EntryPoint>]
let main argv =
 
    let plans = 
        [ 
            // planID dimensionId         name                                unitOfMeasure   includedAnnually/Monthly
            "plan1 | nodecharge         | Per Node Connected                | node/hour"
            "plan1 | cpucharge          | Per CPU urage                     | cpu/hour"
            "plan1 | datasourcecharge   | Per DataSource Integration        | ds/hour"
            "plan1 | messagecharge      | Per Message Transmitted           | message/hour"
            "plan2 | MachineLearningJob | An expensive machine learning job | machine learning jobs | 0/10"
            "plan2 | EMailCampaign      | An e-mail sent for campaign usage | e-mails               | 0/250000" // 0 annually, 250000 monthly
        ] |> parsePlans

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

    let myExtraCoders = Extra.empty |> Json.enrich
    let json = Encode.Auto.toString (1, oldBalance, extra = myExtraCoders)
    let f2 = Decode.Auto.fromString<MeteringState>(json, extra = myExtraCoders)
    printfn "%s \n%A" json f2


    // Position read pointer in EventHub to 237492750, and start applying 
    let eventsFromEventHub = 
        [
            "2021-10-13--14-12-02 | ml    |   1 | Department=Data Science, Project ID=Skunkworks vNext"
            "2021-10-13--15-12-03 | ml    |   2                                                       "
            "2021-10-13--15-13-02 | email | 300 | Email Campaign=User retention, Department=Marketing "
            "2021-10-13--15-12-08 | ml    |  20                                                       "
        ]
        |> parseUsageEvents

    let newBalance =
        eventsFromEventHub
        |> CurrentBillingState.applyUsageEvents oldBalance 

    //printfn "plan %A" plan
    //printfn "usageEvents %A" usageEvents
    //printfn "oldBalance %A" oldBalance.CurrentMeterValues


    //printfn "newBalance %A" (Newtonsoft.Json.JsonConvert.SerializeObject(newBalance))
    0

