﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

module foo

open System
open System.Net.Http
open System.Threading
open Metering.BaseTypes
open Metering.BaseTypes.EventHub
open Metering.Integration

//let id1 = MarketplaceResourceId.fromResourceID "fdc778a6-1281-40e4-cade-4a5fc11f5440"
//let id2 = MarketplaceResourceId.fromResourceURI "/subscriptions/..../resourceGroups/.../providers/Microsoft.SaaS/resources/SaaS Accelerator Test Subscription"
//let id3 = MarketplaceResourceId.from "/subscriptions/..../resourceGroups/.../providers/Microsoft.SaaS/resources/SaaS Accelerator Test Subscription" "fdc778a6-1281-40e4-cade-4a5fc11f5440"
//printfn "%A %A %A" id1 id2 id3

let parseConsumptionEvents (str: string) = 
    let multilineParse parser (str : string) =  
        str
        |> (fun s -> s.Split([|"\n"|], StringSplitOptions.RemoveEmptyEntries))
        |> Array.toList
        |> parser

    let dummyEventsToCatchUp dateStr =
        { NumberOfEvents = 1L
          TimeDeltaSeconds = 0.0
          LastSequenceNumber = 100L
          LastEnqueuedTime = dateStr |> MeteringDateTime.fromStr }
        |> Some

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
                | [sequencenr; datestr; marketplaceResourceId; name; amountstr; props] -> 
                    Some <| EventHubEvent<MeteringUpdateEvent>.createEventHub
                        ({ MarketplaceResourceId = marketplaceResourceId |> MarketplaceResourceId.fromStr
                           Timestamp = datestr |> MeteringDateTime.fromStr 
                           MeterName = name |> ApplicationInternalMeterName.create
                           Quantity = amountstr |> UInt32.Parse |> Quantity.create
                           Properties = props |> parseProps } |> UsageReported)
                        { PartitionID = "1" |> PartitionID.create
                          SequenceNumber = sequencenr |> Int64.Parse
                          PartitionTimestamp = datestr |> MeteringDateTime.fromStr }
                        (dummyEventsToCatchUp datestr)
                | [sequencenr; datestr; marketplaceResourceId; name; amountstr] ->
                    Some <| EventHubEvent<MeteringUpdateEvent>.createEventHub
                        ({ MarketplaceResourceId = marketplaceResourceId |> MarketplaceResourceId.fromStr
                           Timestamp = datestr |> MeteringDateTime.fromStr
                           MeterName = name |> ApplicationInternalMeterName.create
                           Quantity = amountstr |> UInt32.Parse |> Quantity.create
                           Properties = None } |> UsageReported )
                        { PartitionID = "1" |> PartitionID.create
                          SequenceNumber = sequencenr |> Int64.Parse
                          PartitionTimestamp = datestr |> MeteringDateTime.fromStr }
                        (dummyEventsToCatchUp datestr)
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

let inspecto (header: string) (a: 'a) : 'a =
    if String.IsNullOrEmpty header 
    then printfn "%A" a
    else printfn "%s: %A" header a
    
    a

let demoAggregation =
    let parseSub sequence json = 
        json
        |> Json.fromStr<SubscriptionCreationInformation> 
        |> SubscriptionPurchased
        |> (fun mue -> 
            EventHubEvent<MeteringUpdateEvent>.createEventHub
                mue
                { PartitionID = "1" |> PartitionID.create
                  SequenceNumber = sequence
                  PartitionTimestamp = "2021-11-05T10:00:25.7798568Z" |> MeteringDateTime.fromStr }
                ({ LastSequenceNumber = 100L
                   LastEnqueuedTime= "2021-11-05T10:00:25.7798568Z" |> MeteringDateTime.fromStr
                   NumberOfEvents = 1
                   TimeDeltaSeconds = 1.0 } |> Some)
            // |> Some
        )

    // 11111111-8a88-4a47-a691-1b31c289fb33 is a sample GUID of a SaaS subscription
    let sub1 = 
        """
{
	"subscription": {
		"renewalInterval": "Monthly",
		"subscriptionStart": "2021-10-01T12:20:33",
		"scope": "11111111-8a88-4a47-a691-1b31c289fb33",
		"plan": {
			"planId": "plan2",
            "billingDimensions": [
				{ "name": "ml",    "type": "simple", "dimension": "MachineLearningJob", "included": 10 },
				{ "name": "email", "type": "simple", "dimension": "EMailCampaign",      "included": "250000" }
			]     
		}
	}
}
        """ |> parseSub 1

    let sub2 =
        """
{
	"subscription": {
		"renewalInterval": "Monthly",
		"subscriptionStart": "2021-10-13T09:20:33",
		"scope": "22222222-8a88-4a47-a691-1b31c289fb33",
		"plan": {
			"planId": "plan2",
			"billingDimensions": [
				{ "name": "ml",    "type": "simple", "dimension": "MachineLearningJob", "included": 10 },
				{ "name": "email", "type": "simple", "dimension": "EMailCampaign",      "included": "250000" }
			]
		}
	}
}
        """ |> parseSub 2


    let sub3 =
        """
{
	"subscription": {
		"renewalInterval": "Monthly",
		"subscriptionStart": "2021-11-04T16:12:26",
		"scope": "fdc778a6-1281-40e4-cade-4a5fc11f5440",
		"plan": {
			"planId": "free_monthly_yearly",
            "billingDimensions": [
                { "name": "nde", "type": "simple", "dimension": "nodecharge",       "included": 1000       },
                { "name": "cpu", "type": "simple", "dimension": "cpucharge",        "included": "1000"},
                { "name": "dta", "type": "simple", "dimension": "datasourcecharge", "included": 1000       },
                { "name": "obj", "type": "simple", "dimension": "objectcharge",     "included": 1000      },
                { "name": "msg", "type": "simple", "dimension": "messagecharge",    "included": "10000"    }
            ]
		}
	}
}        
        """ |> parseSub 3


    // 11111111-8a88-4a47-a691-1b31c289fb33 2021-10-01T12:20:34
    // 22222222-8a88-4a47-a691-1b31c289fb33 2021-10-13T09:20:36
    // fdc778a6-1281-40e4-cade-4a5fc11f5440 2021-11-04T16:12:26
    // 8151a707-467c-4105-df0b-44c3fca5880d

    // Position read pointer in EventHub to 001002, and start applying 
    let consumptionEvents = 
        """
        04 | 2021-10-13T14:12:02 | 11111111-8a88-4a47-a691-1b31c289fb33 | ml    |      1 | Department=Data Science, Project ID=Skunkworks vNext
        05 | 2021-10-13T15:12:03 | 11111111-8a88-4a47-a691-1b31c289fb33 | ml    |      2
        06 | 2021-10-13T15:13:02 | 11111111-8a88-4a47-a691-1b31c289fb33 | email |    300 | Email Campaign=User retention, Department=Marketing
        07 | 2021-10-13T15:19:02 | 11111111-8a88-4a47-a691-1b31c289fb33 | email | 300000 | Email Campaign=User retention, Department=Marketing
        08 | 2021-10-13T16:01:01 | 11111111-8a88-4a47-a691-1b31c289fb33 | email |      1 | Email Campaign=User retention, Department=Marketing
        09 | 2021-10-13T16:20:01 | 11111111-8a88-4a47-a691-1b31c289fb33 | email |      1 | Email Campaign=User retention, Department=Marketing
        10 | 2021-10-13T17:01:01 | 11111111-8a88-4a47-a691-1b31c289fb33 | email |      1 | Email Campaign=User retention, Department=Marketing
        11 | 2021-10-13T17:01:02 | 11111111-8a88-4a47-a691-1b31c289fb33 | email |     10 | Email Campaign=User retention, Department=Marketing
        12 | 2021-10-15T00:00:02 | 11111111-8a88-4a47-a691-1b31c289fb33 | email |     10 | Email Campaign=User retention, Department=Marketing
        13 | 2021-10-15T01:01:02 | 11111111-8a88-4a47-a691-1b31c289fb33 | email |     10 
        14 | 2021-10-15T01:01:02 | 22222222-8a88-4a47-a691-1b31c289fb33 | email |     10 
        15 | 2021-10-15T01:01:03 | 22222222-8a88-4a47-a691-1b31c289fb33 | ml    |     11
        16 | 2021-10-15T03:01:02 | 22222222-8a88-4a47-a691-1b31c289fb33 | email |     10 
        17 | 2021-10-16T01:01:03 | 22222222-8a88-4a47-a691-1b31c289fb33 | ml    |     8
        18 | 2021-10-16T12:01:03 | 22222222-8a88-4a47-a691-1b31c289fb33 | ml    |     1
        19 | 2021-11-05T09:12:30 | fdc778a6-1281-40e4-cade-4a5fc11f5440 | dta   |     3
        20 | 2021-11-05T09:12:30 | fdc778a6-1281-40e4-cade-4a5fc11f5440 | cpu   |     30001
        """ |> parseConsumptionEvents
    
    let eventsFromEventHub = [ [sub1; sub2; sub3]; consumptionEvents ] |> List.concat // The first event must be the subscription creation, followed by many consumption events

    eventsFromEventHub
    |> MeterCollectionLogic.handleMeteringEvents MeterCollection.Uninitialized
    |> Json.toStr 2
    |> inspect ""
    |> Json.fromStr<MeterCollection>
    |> inspecto "newBalance"
    |> MeterCollectionLogic.usagesToBeReported
    |> Json.toStr 2
    |> inspect "usage"
    |> ignore

    eventsFromEventHub

/// Demonstrates a real marketplace API invocation
//let demoUsageSubmission config =
//    let SomeValidSaaSSubscriptionID = "fdc778a6-1281-40e4-cade-4a5fc11f5440"

//    let usage =
//        { MarketplaceResourceId = MarketplaceResourceId.fromStr SomeValidSaaSSubscriptionID
//          Quantity = Quantity.createFloat 2.3m
//          PlanId = "free_monthly_yearly" |> PlanId.create
//          DimensionId = "datasourcecharge" |> DimensionId.create
//          EffectiveStartTime = "2021-11-29T17:00:00Z" |> MeteringDateTime.fromStr }
//
//    let result = (MarketplaceClient.submitUsage config usage).Result
//
//    result
//    |> Json.toStr 2
//    |> inspect "MarketplaceSubmissionResult"
//    |> Json.fromStr<MarketplaceSubmissionResult>
//    |> inspecto ""
//    |> ignore

let demoStorage (meteringConnections: MeteringConnections) eventsFromEventHub =
    let events = 
        eventsFromEventHub
        |> MeterCollectionLogic.handleMeteringEvents MeterCollection.Uninitialized // We start completely uninitialized
        |> Json.toStr 1                             |> inspect "meters"
        |> Json.fromStr<MeterCollection>              // |> inspect "newBalance"
        
    (task {
        let! () = MeterCollectionStore.storeLastState meteringConnections events CancellationToken.None 

        let partitionId = 
            Some events
            |> MeterCollectionLogic.lastUpdate
            |> (fun x -> x.Value.PartitionID)

        let! meters = MeterCollectionStore.loadLastState meteringConnections partitionId CancellationToken.None

        match meters with
        | Some meter -> 
            meter
            |> inspecto "read"
            |> Json.toStr 4
            |> ignore
        | None -> printfn "No state found"

        return ()
    }).Wait()

[<EntryPoint>]
let main argv = 
    let usage : MeteringUpdateEvent = 
        { MarketplaceResourceId = MarketplaceResourceId.fromStr "/subscriptions/.../resourceGroups/customer-owned-rg/providers/Microsoft.Solutions/applications/myapp123"
          Timestamp = MeteringDateTime.now()
          MeterName = ApplicationInternalMeterName.create "cpu"
          Quantity = Quantity.create 10u
          Properties = None } |> UsageReported 

    usage
    |> Json.toStr 5
    |> (fun x -> printfn "%s" x; x)
    |> Json.fromStr<MeteringUpdateEvent>
    |> (fun x -> printfn "%A" x; x)
    |> ignore

    
    //demoUsageSubmission config

    demoAggregation 
    |> Json.toStr 1
    |> (fun x -> printfn "%A" x)
    //demoStorage config eventsFromEventHub

    0