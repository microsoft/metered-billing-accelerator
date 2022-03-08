// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

module foo

open System
open System.Net.Http
open Metering.BaseTypes
open Metering.Integration
open System.Threading

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
                | [sequencenr; datestr; internalResourceId; name; amountstr; props] -> 
                    Some <| MeteringEvent.create
                        ({ InternalResourceId = internalResourceId |> InternalResourceId.fromStr
                           Timestamp = datestr |> MeteringDateTime.fromStr 
                           MeterName = name |> ApplicationInternalMeterName.create
                           Quantity = amountstr |> UInt32.Parse |> Quantity.createInt
                           Properties = props |> parseProps } |> UsageReported)
                        { PartitionID = "1" |> PartitionID.create
                          SequenceNumber = sequencenr |> Int64.Parse
                          PartitionTimestamp = datestr |> MeteringDateTime.fromStr }
                        (dummyEventsToCatchUp datestr)
                | [sequencenr; datestr; internalResourceId; name; amountstr] ->
                    Some <| MeteringEvent.create
                        ({ InternalResourceId = internalResourceId |> InternalResourceId.fromStr
                           Timestamp = datestr |> MeteringDateTime.fromStr
                           MeterName = name |> ApplicationInternalMeterName.create
                           Quantity = amountstr |> UInt32.Parse |> Quantity.createInt
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

let demoAggregation (config: MeteringConfigurationProvider) =
    let parseSub = 
        Json.fromStr<SubscriptionCreationInformation> 
        >> SubscriptionPurchased
        >> (fun mue -> 
            MeteringEvent.create
                mue
                { PartitionID = "1" |> PartitionID.create
                  SequenceNumber = 1L
                  PartitionTimestamp = "2021-11-05T10:00:25.7798568Z" |> MeteringDateTime.fromStr }
                ({ LastSequenceNumber = 100L
                   LastEnqueuedTime= "2021-11-05T10:00:25.7798568Z" |> MeteringDateTime.fromStr
                   NumberOfEvents = 1
                   TimeDeltaSeconds = 1.0 } |> Some)
            // |> Some
        )

    // 11111111-8a88-4a47-a691-1b31c289fb33 is a sample GUID of a SaaS subscription
    let sub1 =
        """{
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
    }""" |> parseSub

    let sub2 =
        """{
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
    }""" |> parseSub


    let sub3 =
        """{
     "subscription": {
           "renewalInterval": "Monthly",
           "subscriptionStart": "2021-11-04T16:12:26",
           "scope": "fdc778a6-1281-40e4-cade-4a5fc11f5440",
           "plan": {
             "planId": "free_monthly_yearly",
             "billingDimensions": [
               { "dimension": "nodecharge", "name": "Per Node Connected", "unitOfMeasure": "node/hour", "includedQuantity": { "monthly": "1000", "annually": "10000" } },
               { "dimension": "cpucharge", "name": "Per CPU Connected", "unitOfMeasure": "cpu/hour", "includedQuantity": { "monthly": "1000", "annually": "10000" } },
               { "dimension": "datasourcecharge", "name": "Per Datasource Integration", "unitOfMeasure": "ds/hour", "includedQuantity": { "monthly": "1000", "annually": "10000" } },
               { "dimension": "messagecharge", "name": "Per Message Transmitted", "unitOfMeasure": "message/hour", "includedQuantity": { "monthly": "1000", "annually": "10000" } },
               { "dimension": "objectcharge", "name": "Per Object Detected", "unitOfMeasure": "object/hour", "includedQuantity": { "monthly": "1000", "annually": "10000" } } ] } },
     "metersMapping": { "nde": "nodecharge", "cpu": "cpucharge", "dta": "datasourcecharge", "msg": "messagecharge", "obj": "objectcharge"}
    }""" |> parseSub


    // 11111111-8a88-4a47-a691-1b31c289fb33 2021-10-01T12:20:34
    // 22222222-8a88-4a47-a691-1b31c289fb33 2021-10-13T09:20:36
    // fdc778a6-1281-40e4-cade-4a5fc11f5440 2021-11-04T16:12:26
    // 8151a707-467c-4105-df0b-44c3fca5880d

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
        001015 | 2021-10-15T01:01:03 | 22222222-8a88-4a47-a691-1b31c289fb33 | ml    |     11
        001016 | 2021-10-15T03:01:02 | 22222222-8a88-4a47-a691-1b31c289fb33 | email |     10 
        001017 | 2021-10-16T01:01:03 | 22222222-8a88-4a47-a691-1b31c289fb33 | ml    |     8
        001018 | 2021-10-16T12:01:03 | 22222222-8a88-4a47-a691-1b31c289fb33 | ml    |     1
        001019 | 2021-11-05T09:12:30 | fdc778a6-1281-40e4-cade-4a5fc11f5440 | dta   |     3
        001020 | 2021-11-05T09:12:30 | fdc778a6-1281-40e4-cade-4a5fc11f5440 | cpu   |     30001
        """ |> parseConsumptionEvents
    
    let eventsFromEventHub = [ [sub1; sub2; sub3]; consumptionEvents ] |> List.concat // The first event must be the subscription creation, followed by many consumption events

    eventsFromEventHub
    |> MeterCollectionLogic.handleMeteringEvents config.TimeHandlingConfiguration MeterCollection.Uninitialized
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
//        { ResourceId = InternalResourceId.fromStr SomeValidSaaSSubscriptionID
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

let demoStorage (config: MeteringConfigurationProvider) eventsFromEventHub =
    let events = 
        eventsFromEventHub
        |> MeterCollectionLogic.handleMeteringEvents config.TimeHandlingConfiguration MeterCollection.Uninitialized // We start completely uninitialized
        |> Json.toStr 1                             |> inspect "meters"
        |> Json.fromStr<MeterCollection>              // |> inspect "newBalance"
        
    (task {
        let! () = MeterCollectionStore.storeLastState config events CancellationToken.None 

        let partitionId = 
            Some events
            |> MeterCollectionLogic.lastUpdate
            |> (fun x -> x.Value.PartitionID)

        let! meters = MeterCollectionStore.loadLastState config partitionId CancellationToken.None

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
        { InternalResourceId = ManagedApplication ManagedAppIdentity 
          Timestamp = MeteringDateTime.now()
          MeterName = ApplicationInternalMeterName.create "cpu"
          Quantity = Quantity.createInt 10u
          Properties = None } |> UsageReported 

    usage
    |> Json.toStr 5
    |> (fun x -> printfn "%s" x; x)
    |> Json.fromStr<MeteringUpdateEvent>
    |> (fun x -> printfn "%A" x; x)
    |> ignore

    //let config = 
    //    { CurrentTimeProvider = CurrentTimeProvider.LocalSystem
    //      SubmitMeteringAPIUsageEvent = SubmitMeteringAPIUsageEvent.Discard
    //      GracePeriod = Duration.FromHours(6.0)
    //      ManagedResourceGroupResolver = ManagedAppResourceGroupID.retrieveDummyID "/subscriptions/deadbeef-stuff/resourceGroups/somerg"
    //      MeteringConnections = MeteringConnections.getFromEnvironment() }

    //demoUsageSubmission config

    //let eventsFromEventHub = demoAggregation config
    //demoStorage config eventsFromEventHub

    0