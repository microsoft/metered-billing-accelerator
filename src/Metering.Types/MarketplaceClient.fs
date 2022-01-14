namespace Metering.Types

open System
open System.Net.Http
open System.Text
open System.Threading.Tasks
open System.Runtime.CompilerServices

[<Extension>]
module MarketplaceClient =
    let private meteringApiVersion = "2018-08-31"

    /// The batch usage event API allows you to emit usage events for more than one purchased resource at once. 
    /// It also allows you to emit several usage events for the same resource as long as they are for different calendar hours. 
    /// The maximal number of events in a single batch is 25.
    let submitBatchUsage (config: MeteringConfigurationProvider) (usage: MarketplaceRequest list) : Task<MarketplaceBatchResponse> = 
        task {
            let! client = InstanceMetadataClient.createMarketplaceClient config.MeteringConnections.MeteringAPICredentials
            let json = usage |> MarketplaceBatchRequest.createBatch |> Json.toStr 0
    
            let request = new HttpRequestMessage(
                method = HttpMethod.Post, 
                requestUri = $"/api/batchUsageEvent?api-version={meteringApiVersion}",
                Content = new StringContent(json, Encoding.UTF8, "application/json"))
          
            let! response = client.SendAsync(request)
            let! responseJson = response.Content.ReadAsStringAsync()
    
            let header name = String.concat " " (seq <| response.Headers.GetValues(name))
            let azureHeader = { RequestID = header "x-ms-requestid"; CorrelationID = header "x-ms-correlationid" }
    
            return 
                responseJson
                |> Json.fromStr<MarketplaceBatchResponseDTO>
                |> (fun x -> x.Results)
                |> List.map (MarketplaceResponse.create azureHeader)
                |> MarketplaceBatchResponse.create
        }

    let submitUsagesCsharp : Func<MeteringConfigurationProvider, MarketplaceRequest list, Task<MarketplaceBatchResponse>> = submitBatchUsage

    [<Extension>]
    let SubmitUsage (config: MeteringConfigurationProvider) (usage: MarketplaceRequest seq) : Task<MarketplaceBatchResponse> = 
        submitBatchUsage config (usage |> Seq.toList) 

    //let submitUsage (config: MeteringConfigurationProvider) (usage: MarketplaceRequest) : Task<MarketplaceSubmissionResult> = 
    //    task {
    //        let! client = InstanceMetadataClient.createMarketplaceClient config.MeteringConnections.MeteringAPICredentials
    //        let json = usage |> Json.toStr 0
    //
    //        let request = new HttpRequestMessage(
    //            method = HttpMethod.Post, 
    //            requestUri = $"/api/usageEvent?api-version={meteringApiVersion}",
    //            Content = new StringContent(json, Encoding.UTF8, "application/json"))
    //
    //        let! response = client.SendAsync(request)
    //        let! json = response.Content.ReadAsStringAsync()
    //
    //        let header name = String.concat " " (seq <| response.Headers.GetValues(name))
    //
    //        let azureHeader = 
    //            { RequestID = header "x-ms-requestid" 
    //              CorrelationID = header "x-ms-correlationid"}
    //
    //        let result =
    //            match response.StatusCode with
    //            | HttpStatusCode.OK -> 
    //                json |> Json.fromStr<MarketplaceSubmissionAcceptedResponse> |> Ok
    //            | HttpStatusCode.Conflict -> json |> MarketplaceSubmissionError.Duplicate |> Error
    //            | HttpStatusCode.BadRequest ->
    //                try
    //                    let jsonBody = JsonDocument.Parse(json)
    //                    // I'm not proud of this
    //                    match ((jsonBody.RootElement.GetProperty("details"))[0]).GetProperty("target").GetString() with
    //                    | "resourceId" -> json |> BadResourceId |> Error
    //                    | "effectiveStartTime" -> json |> InvalidEffectiveStartTime |> Error
    //                    | _ -> json |> CommunicationsProblem |> Error
    //                with 
    //                | _ -> json |> CommunicationsProblem |> Error
    //            | _ -> json |> CommunicationsProblem |> Error
    //    
    //        return { Payload = usage; Result = result; Headers = azureHeader }
    //    }
    // let submitUsageCsharp : Func<MeteringConfigurationProvider, MeteringAPIUsageEventDefinition, Task<MarketplaceSubmissionResult>> = submitUsage
