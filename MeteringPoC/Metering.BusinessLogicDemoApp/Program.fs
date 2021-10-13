open System
open Metering.Types
open System.Globalization

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
        let parseQuantity(p: string) : IncludedQuantityMonthly option =
            UInt64.Parse(p)
            |> Some

        s.Split([|'|'|], 5)
        |> Array.toList
        |> List.map (fun s -> s.Trim())
        |> function
            | [planId; dimensionId; name; unitOfMeasure; includedQuantity] -> 
                (planId, {
                    DimensionIdentifier = dimensionId
                    DimensionName = name
                    UnitOfMeasure = unitOfMeasure
                    IncludedQuantityMonthly = includedQuantity |> parseQuantity
                })
            | [planId; dimensionId; name; unitOfMeasure] -> 
                (planId, {
                    DimensionIdentifier = dimensionId
                    DimensionName = name
                    UnitOfMeasure = unitOfMeasure
                    IncludedQuantityMonthly = None
                })
            | _ -> failwith "parsing error"

    planStrings
    |> Seq.map parseBillingDimension
    |> Seq.groupBy(fun (plan, _) -> plan)
    |> Seq.map(fun (plan, elems) -> (plan, (elems |> Seq.map(fun (p, e) -> e) ) ))
    |> Seq.map(fun (planid, billingDims) -> { PlanId = planid; BillingDimensions = billingDims })

let parseUsageEvents events =
    let parseUsageEvent (s: string) : UsageEvent option =
        let parseDate (p: string) : DateTime =
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
            | [datestr; planId; name; amountstr; props] -> 
                Some {
                    PlanId = planId
                    DimensionIdentifier = name
                    Timestamp = datestr |> parseDate
                    Quantity = amountstr |> UInt64.Parse
                    Properties = props |> parseProps
                }
            | [datestr; planId; name; amountstr] -> 
                Some {
                    PlanId = planId
                    DimensionIdentifier = name
                    Timestamp = datestr |> parseDate
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
            "plan1 | nodecharge         | Per Node Connected                | node/hour"
            "plan1 | cpucharge          | Per CPU urage                     | cpu/hour"
            "plan1 | datasourcecharge   | Per DataSource Integration        | ds/hour"
            "plan1 | messagecharge      | Per Message Transmitted           | message/hour"
            "plan2 | MachineLearningJob | An expensive machine learning job | machine learning jobs | 10"
            "plan2 | EMailCampaign      | An e-mail sent for campaign usage | e-mails               | 250000"
        ] |> parsePlans
        
    let oldBalance  = {
        Plans = plans
        InitialPurchase = {
            PlanId = "plan2"
            PurchaseTimestamp = DateTime.UtcNow.Subtract(TimeSpan.FromHours(26.0)) }
        CurrentCredits =
            [
                ({ PlanId = "plan2"; DimensionIdentifier = "EMailCampaign" }, ConsumedQuantity(100UL))
                ({ PlanId = "plan2"; DimensionIdentifier = "MachineLearningJob"}, RemainingQuantity(10UL))
            ] |> Map.ofList
    }

    let newBalance =
        [
            "2021-10-13--14-12-02 | plan2 | MachineLearningJob |   1 | Department=Data Science, Project ID=Skunkworks vNext"
            "2021-10-13--15-12-02 | plan2 | MachineLearningJob |   2                                                       "
            "2021-10-13--15-13-02 | plan2 | EMailCampaign      | 300 | Email Campaign=User retention, Department=Marketing "
        ]
        |> parseUsageEvents
        |> BusinessLogic.applyUsageEvents oldBalance 

    //printfn "plan %A" plan
    //printfn "usageEvents %A" usageEvents
    printfn "oldBalance %A" oldBalance.CurrentCredits
    printfn "newBalance %A" newBalance.CurrentCredits

    //printfn "newBalance %A" (Newtonsoft.Json.JsonConvert.SerializeObject(newBalance))
    0

