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
    
            s.Split([|'|'|], 5)
            |> Array.toList
            |> List.map (fun s -> s.Trim())
            |> function
                | [sequencenr; datestr; name; amountstr; props] -> 
                    Some {
                        MeteringUpdateEvent = UsageReported {
                            Timestamp = datestr |> MeteringDateTime.fromStr 
                            MeterName = name
                            Quantity = amountstr |> UInt64.Parse
                            Properties = props |> parseProps }
                        MessagePosition = {
                            PartitionID = "1"
                            SequenceNumber = sequencenr |> UInt64.Parse
                            PartitionTimestamp = datestr |> MeteringDateTime.fromStr }
                    }
                | [sequencenr; datestr; name; amountstr] -> 
                    Some {
                        MeteringUpdateEvent = UsageReported {
                            Timestamp = datestr |> MeteringDateTime.fromStr
                            MeterName = name
                            Quantity = amountstr |> UInt64.Parse
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

let myExtraCoders = Extra.empty |> Json.enrich

let jsonDecode<'T> json = 
    match Decode.Auto.fromString<'T>(json, extra = myExtraCoders) with
    | Ok r -> r
    | Result.Error e -> failwith e

let jsonEncode o = Encode.Auto.toString(4, o, extra = myExtraCoders)

let inspect a =
    printfn "%s" a
    a

[<EntryPoint>]
let main argv = 
    let subscriptionCreationEvent =
         """
{
  "MeteringUpdateEvent": {
    "type": "subscriptionPurchased",
    "value": {
      "subscription": {
        "renewalInterval": "Monthly",
        "subscriptionStart": "2021-10-01--12-20-33",
        "plan": {
          "planId": "plan2",
          "billingDimensions": [
            { "dimension": "MachineLearningJob", "name": "An expensive machine learning job", "unitOfMeasure": "machine learning jobs", "includedQuantity": { "monthly": "10" } },
            { "dimension": "EMailCampaign", "name": "An e-mail sent for campaign usage", "unitOfMeasure": "e-mails", "includedQuantity": { "monthly": "250000" } } ] } },
      "metersMapping": { "email": "EMailCampaign", "ml": "MachineLearningJob" }
    }
  },
  "MessagePosition": {
    "partitionTimestamp": "2021-10-01--12-20-34",
    "sequenceNumber": "1",
    "partitionId": "1"
  }
}
    """ |> jsonDecode<MeteringEvent>

    // Position read pointer in EventHub to 001002, and start applying 
    let consumptionEvents = 
        """
        001002 | 2021-10-13--14-12-02 | ml    |      1 | Department=Data Science, Project ID=Skunkworks vNext
        001003 | 2021-10-13--15-12-03 | ml    |      2
        001004 | 2021-10-13--15-13-02 | email |    300 | Email Campaign=User retention, Department=Marketing
        001007 | 2021-10-13--15-19-02 | email | 300000 | Email Campaign=User retention, Department=Marketing
        001008 | 2021-10-13--16-01-01 | email |      1 | Email Campaign=User retention, Department=Marketing
        001009 | 2021-10-13--16-20-01 | email |      1 | Email Campaign=User retention, Department=Marketing
        001010 | 2021-10-13--17-01-01 | email |      1 | Email Campaign=User retention, Department=Marketing
        001011 | 2021-10-13--17-01-02 | email |     10 | Email Campaign=User retention, Department=Marketing
        001012 | 2021-10-15--00-00-02 | email |     10 | Email Campaign=User retention, Department=Marketing
        001012 | 2021-10-15--01-01-02 | email |     10 
        """ |> parseConsumptionEvents
        
    let eventsFromEventHub = subscriptionCreationEvent :: consumptionEvents // The first event must be the subscription creation, followed by many consumption events

    let emptyBalance : MeteringState option = None // We start completely uninitialized

    let config = 
        { CurrentTimeProvider = CurrentTimeProvider.LocalSystem
          SubmitMeteringAPIUsageEvent = SubmitMeteringAPIUsageEvent.Discard
          GracePeriod = Duration.FromHours(6.0) }

    eventsFromEventHub
    |> Logic.handleEvents config emptyBalance
    |> jsonEncode                             |> inspect
    |> jsonDecode<MeteringState>              // |> inspect "newBalance"
    |> ignore

    0