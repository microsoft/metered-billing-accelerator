open System
open Thoth.Json.Net

open Metering
open Metering.Types
open Metering.Types.EventHub
open NodaTime

let parseConsumptionEvents (str: string) = 
    let multilineParse parser (str : string) =  
        str
        |> (fun s -> s.Split([|"\n"|], StringSplitOptions.RemoveEmptyEntries))
        |> Array.toList
        |> parser

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
    
            s.Split([|'|'|], 6)
            |> Array.toList
            |> List.map (fun s -> s.Trim())
            |> function
                | [sequencenr; datestr; scope; name; amountstr; props] -> 
                    Some {
                        MeteringUpdateEvent = UsageReported {
                            Scope = scope |> SubscriptionType.fromStr
                            Timestamp = datestr |> MeteringDateTime.fromStr 
                            MeterName = name |> ApplicationInternalMeterName.create
                            Quantity = amountstr |> UInt64.Parse |> Quantity.createInt
                            Properties = props |> parseProps }
                        MessagePosition = {
                            PartitionID = "1"
                            SequenceNumber = sequencenr |> UInt64.Parse
                            PartitionTimestamp = datestr |> MeteringDateTime.fromStr }
                    }
                | [sequencenr; datestr; scope; name; amountstr] -> 
                    Some {
                        MeteringUpdateEvent = UsageReported {
                            Scope = scope |> SubscriptionType.fromStr
                            Timestamp = datestr |> MeteringDateTime.fromStr
                            MeterName = name |> ApplicationInternalMeterName.create
                            Quantity = amountstr |> UInt64.Parse |> Quantity.createInt
                            Properties = None }
                        MessagePosition = {
                            PartitionID = "1"
                            SequenceNumber = sequencenr |> UInt64.Parse
                            PartitionTimestamp = datestr |> MeteringDateTime.fromStr }
                    }
                | _ -> None
        events
        |> List.map parseUsageEvent
        |> List.choose id

    str
    |> multilineParse parseUsageEvents



let inspect header a =
    if String.IsNullOrEmpty header 
    then printfn "%s" a
    else printfn "%s: %s" header a
    
    a

[<EntryPoint>]
let main argv = 

    // 11111111-8a88-4a47-a691-1b31c289fb33 is a sample GUID of a SaaS subscription
    let sub1 =
        """
{
  "MeteringUpdateEvent": {
    "type": "subscriptionPurchased",
    "value": {
     "subscription": {
       "renewalInterval": "Monthly",
       "subscriptionStart": "2021-10-01T12:20:33",
       "scope": "11111111-8a88-4a47-a691-1b31c289fb33",
       "plan": {
         "planId": "plan2",
         "billingDimensions": [
           { "dimension": "MachineLearningJob", "name": "An expensive machine learning job", "unitOfMeasure": "machine learning jobs", "includedQuantity": { "monthly": "10" } },
           { "dimension": "EMailCampaign", "name": "An e-mail sent for campaign usage", "unitOfMeasure": "e-mails", "includedQuantity": { "monthly": "250000" } } ] } },
     "metersMapping": { "email": "EMailCampaign", "ml": "MachineLearningJob" }
    }
  },
  "MessagePosition": {
    "partitionTimestamp": "2021-10-01T12:20:34",
    "sequenceNumber": "1",
    "partitionId": "1"
  }
}
    """ |> Json.fromStr<MeteringEvent>

    let sub2 =
        """
{
  "MeteringUpdateEvent": {
    "type": "subscriptionPurchased",
    "value": {
     "subscription": {
       "renewalInterval": "Monthly",
       "subscriptionStart": "2021-10-13T09:20:33",
       "scope": "22222222-8a88-4a47-a691-1b31c289fb33",
       "plan": {
         "planId": "plan2",
         "billingDimensions": [
           { "dimension": "MachineLearningJob", "name": "An expensive machine learning job", "unitOfMeasure": "machine learning jobs", "includedQuantity": { "monthly": "10" } },
           { "dimension": "EMailCampaign", "name": "An e-mail sent for campaign usage", "unitOfMeasure": "e-mails", "includedQuantity": { "monthly": "250000" } } ] } },
     "metersMapping": { "email": "EMailCampaign", "ml": "MachineLearningJob" }
    }
  },
  "MessagePosition": {
    "partitionTimestamp": "2021-10-13T09:20:36",
    "sequenceNumber": "1",
    "partitionId": "1"
  }
}
    """ |> Json.fromStr<MeteringEvent>
    

    // 11111111-8a88-4a47-a691-1b31c289fb33 2021-10-01T12:20:34
    // 22222222-8a88-4a47-a691-1b31c289fb33 2021-10-13T09:20:36


    // Position read pointer in EventHub to 001002, and start applying 
    let consumptionEvents = 
        """
        001002 | 2021-10-13T14:12:02 | 11111111-8a88-4a47-a691-1b31c289fb33 | ml    |      1 | Department=Data Science, Project ID=Skunkworks vNext
        001003 | 2021-10-13T15:12:03 | 11111111-8a88-4a47-a691-1b31c289fb33 | ml    |      2
        001004 | 2021-10-13T15:13:02 | 11111111-8a88-4a47-a691-1b31c289fb33 | email |    300 | Email Campaign=User retention, Department=Marketing
        001007 | 2021-10-13T15:19:02 | 11111111-8a88-4a47-a691-1b31c289fb33 | email | 300000 | Email Campaign=User retention, Department=Marketing
        001008 | 2021-10-13T16:01:01 | 11111111-8a88-4a47-a691-1b31c289fb33 | email |      1 | Email Campaign=User retention, Department=Marketing
        001009 | 2021-10-13T16:20:01 | 11111111-8a88-4a47-a691-1b31c289fb33 | email |      1 | Email Campaign=User retention, Department=Marketing
        001010 | 2021-10-13T17:01:01 | 11111111-8a88-4a47-a691-1b31c289fb33 | email |      1 | Email Campaign=User retention, Department=Marketing
        001011 | 2021-10-13T17:01:02 | 11111111-8a88-4a47-a691-1b31c289fb33 | email |     10 | Email Campaign=User retention, Department=Marketing
        001012 | 2021-10-15T00:00:02 | 11111111-8a88-4a47-a691-1b31c289fb33 | email |     10 | Email Campaign=User retention, Department=Marketing
        001013 | 2021-10-15T01:01:02 | 11111111-8a88-4a47-a691-1b31c289fb33 | email |     10 
        001014 | 2021-10-15T01:01:02 | 22222222-8a88-4a47-a691-1b31c289fb33 | email |     10 
        001014 | 2021-10-15T01:01:03 | 22222222-8a88-4a47-a691-1b31c289fb33 | ml    |     11
        001014 | 2021-10-15T03:01:02 | 22222222-8a88-4a47-a691-1b31c289fb33 | email |     10 
        """ |> parseConsumptionEvents
        
    let eventsFromEventHub = [ [sub1; sub2]; consumptionEvents ] |> List.concat // The first event must be the subscription creation, followed by many consumption events


    let config = 
        { CurrentTimeProvider = CurrentTimeProvider.LocalSystem
          SubmitMeteringAPIUsageEvent = SubmitMeteringAPIUsageEvent.Discard
          GracePeriod = Duration.FromHours(6.0) }

    eventsFromEventHub
    |> MeterCollection.meterCollectionHandleMeteringEvents config MeterCollection.empty // We start completely uninitialized
    |> Json.toStr                             |> inspect "meters"
    |> Json.fromStr<MeterCollection>              // |> inspect "newBalance"
    |> MeterCollection.usagesToBeReported |> Json.toStr |> inspect  "usage"
    |> ignore

    0