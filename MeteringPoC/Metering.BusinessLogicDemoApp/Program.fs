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
           | [datestr; plan; name; amountstr; props] -> 
               Some {
                   PlanID = plan
                   Dimension = name
                   Timestamp = datestr |> parseDate
                   Quantity = amountstr |> UInt64.Parse
                   Properties = props |> parseProps
               }
           | [datestr; plan; name; amountstr] -> 
               Some {
                   PlanID = plan
                   Dimension = name
                   Timestamp = datestr |> parseDate
                   Quantity = amountstr |> UInt64.Parse
                   Properties = None
               }
           | _ -> None

let parseBillingDimension (s: string) : BillingDimension =
    let parseQuantity(p: string) : IncludedQuantityMonthly option =
        UInt64.Parse(p)
        |> Some

    s.Split([|'|'|], 4)
    |> Array.toList
    |> List.map (fun s -> s.Trim())
    |> function
        | [identifier; name; unitOfMeasure; includedQuantity] -> 
            {
                DimensionIdentifier = identifier
                DimensionName = name
                UnitOfMeasure = unitOfMeasure
                IncludedQuantityMonthly = includedQuantity |> parseQuantity
            }
        | [identifier; name; unitOfMeasure] -> 
            {
                DimensionIdentifier = identifier
                DimensionName = name
                UnitOfMeasure = unitOfMeasure
                IncludedQuantityMonthly = None
            }
        | _ -> failwith "parsing error"

[<EntryPoint>]
let main argv =
    let plan = 
        [ 
            "MachineLearningJob   | An expensive machine learning job | machine learning jobs | 10"
            "EMailCampaign        | An e-mail sent for campaign usage | e-mails               | 250000"
            "nodecharge           | Per Node Connected                | node/hour"
            "cpucharge            | Per CPU urage                     | cpu/hour"
            "datasourcecharge     | Per DataSource Integration        | ds/hour"
            "messagecharge        | Per Message Transmitted           | message/hour"
        ]
        |> List.map parseBillingDimension
        |> (fun bd -> { Id = "plan1"; BillingDimensions = bd })

    let usageEvents = 
        [
            "2021-10-13--14-12-02 | plan1 | MachineLearningJob |   1 | Department=Data Science, Project ID=Skunkworks vNext"
            "2021-10-13--15-12-02 | plan1 | MachineLearningJob |   2                                                       "
            "2021-10-13--15-13-02 | plan1 | EMailCampaign      | 300 | Email Campaign=User retention, Department=Marketing "
        ]
        |> List.map parseUsageEvent
        |> List.choose id

    let oldBalance  = {
        Plans = [ plan ]
        InitialPurchase = {
            PlanId = plan.Id
            PurchaseTimestamp = DateTime.UtcNow.Subtract(TimeSpan.FromHours(26.0)) }
        CurrentCredits =
            [
                ("EMailCampaign", ConsumedQuantity(100UL))
                ("MachineLearningJob", RemainingQuantity(10UL))
            ] |> Map.ofList
    }

    let newBalance =
        usageEvents
        |> BusinessLogic.applyUsageEvents oldBalance 

    //printfn "plan %A" plan
    //printfn "usageEvents %A" usageEvents
    printfn "oldBalance %A" oldBalance.CurrentCredits
    printfn "newBalance %A" newBalance.CurrentCredits

    //printfn "newBalance %A" (Newtonsoft.Json.JsonConvert.SerializeObject(newBalance))
    0

