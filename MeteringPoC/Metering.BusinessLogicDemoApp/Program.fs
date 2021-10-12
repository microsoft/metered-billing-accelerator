open System
open Metering.Types

// Plan
// - Containing information about how many widgets are included per month
//
// Purchase
// - Containing information about Billing dates
//
// Current consumption counter
// - Countdown for "included widgets" in current billing period
// - Once "included widgets" for current billing period are consumed, start counting hourly

[<EntryPoint>]
let main argv =
    let plan = {
        Id = "Some really mixed plan"
        BillingDimensions = [ 
            { 
                DimensionIdentifier = "MachineLearningJob"
                DimensionName = "An expensive machine learning job" 
                UnitOfMeasure = "machine learning jobs"
                IncludedQuantityMonthly = 10UL
            }
            { 
                DimensionIdentifier = "EMailCampaign"
                DimensionName = "An e-mail sent for campaign usage" 
                UnitOfMeasure = "e-mails"
                IncludedQuantityMonthly = 250_000UL
            }
        ]
    }

    let usageEvents = [
        {
            PlanID = plan.Id
            Dimension = 
                plan.BillingDimensions
                |> Seq.find (fun a -> a.DimensionIdentifier = "MachineLearningJob")
                |> (fun a -> a.DimensionIdentifier)
            Timestamp =  DateTime.UtcNow.Subtract(TimeSpan.FromMinutes(2.0))
            Quantity = 1UL
            Properties = [
                ("Department", "Data Science")
                ("Project ID", "Skunkworks vNext")
                ] |> Map.ofList |> Some
        }
        {
            PlanID = plan.Id
            Dimension = 
                plan.BillingDimensions
                |> Seq.find (fun a -> a.DimensionIdentifier = "EMailCampaign")
                |> (fun a -> a.DimensionIdentifier)
            Timestamp =  DateTime.UtcNow.Subtract(TimeSpan.FromMinutes(2.0))
            Quantity = 3_000UL
            Properties = [
                ("Email Campaign", "User retention")
                ("Department", "Marketing")
                ] |> Map.ofList |> Some
        }
        {
            PlanID = plan.Id
            Dimension = 
                plan.BillingDimensions
                |> Seq.find (fun a -> a.DimensionIdentifier = "MachineLearningJob")
                |> (fun a -> a.DimensionIdentifier)
            Timestamp =  DateTime.UtcNow.Subtract(TimeSpan.FromMinutes(2.0))
            Quantity = 12UL
            Properties = [
                ("Department", "Data Science")
                ("Project ID", "Skunkworks vNext")
                ] |> Map.ofList |> Some
        }
    ]
    
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

    printfn "oldBalance %A" oldBalance.CurrentCredits
    printfn "newBalance %A" newBalance.CurrentCredits

    //printfn "newBalance %A" (Newtonsoft.Json.JsonConvert.SerializeObject(newBalance))
    0

