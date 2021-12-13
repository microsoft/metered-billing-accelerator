namespace Metering.Types

open System
open System.Net
open System.Net.Http
open System.Text
open System.Text.Json
open System.Threading.Tasks

module MarketplaceClient =
    let submitUsage (config: MeteringConfigurationProvider) (usage: MeteringAPIUsageEventDefinition) : Task<MarketplaceSubmissionResult> = 
        task {
            let! client = InstanceMetadataClient.createMarketplaceClient config.MeteringConnections.MeteringAPICredentials
            let json = usage |> Json.toStr 0
 
            let meteringApiVersion = "2018-08-31"
            let request = new HttpRequestMessage(
                method = HttpMethod.Post, 
                requestUri = $"/api/usageEvent?api-version={meteringApiVersion}",
                Content = new StringContent(json, Encoding.UTF8, "application/json"))

            let! response = client.SendAsync(request)
            let! json = response.Content.ReadAsStringAsync()

            let header name = String.concat " " (seq <| response.Headers.GetValues(name))

            let azureHeader = 
                { RequestID = header "x-ms-requestid" 
                  CorrelationID = header "x-ms-correlationid"}

            let result =
                match response.StatusCode with
                | HttpStatusCode.OK -> 
                    json |> Json.fromStr<MarketplaceSubmissionAcceptedResponse> |> Ok
                | HttpStatusCode.Conflict -> json |> Duplicate |> Error
                | HttpStatusCode.BadRequest ->
                    try
                        let jsonBody = JsonDocument.Parse(json)
                        // I'm not proud of this
                        match ((jsonBody.RootElement.GetProperty("details"))[0]).GetProperty("target").GetString() with
                        | "resourceId" -> json |> BadResourceId |> Error
                        | "effectiveStartTime" -> json |> InvalidEffectiveStartTime |> Error
                        | _ -> json |> CommunicationsProblem |> Error
                    with 
                    | _ -> json |> CommunicationsProblem |> Error
                | _ -> json |> CommunicationsProblem |> Error
            
            return { Payload = usage; Result = result; Headers = azureHeader }
        }

    let submitUsages (config: MeteringConfigurationProvider) (usage: MeteringAPIUsageEventDefinition) : Task<MarketplaceSubmissionResult> = 
        task {
            let! client = InstanceMetadataClient.createMarketplaceClient config.MeteringConnections.MeteringAPICredentials
            let json = usage |> Json.toStr 0
 
            let meteringApiVersion = "2018-08-31"
            let request = new HttpRequestMessage(
                method = HttpMethod.Post, 
                requestUri = $"/api/usageEvent?api-version={meteringApiVersion}",
                Content = new StringContent(json, Encoding.UTF8, "application/json"))

            let! response = client.SendAsync(request)
            let! json = response.Content.ReadAsStringAsync()

            let header name = String.concat " " (seq <| response.Headers.GetValues(name))

            let azureHeader = 
                { RequestID = header "x-ms-requestid" 
                  CorrelationID = header "x-ms-correlationid"}

            let result =
                match response.StatusCode with
                | HttpStatusCode.OK -> 
                    json |> Json.fromStr<MarketplaceSubmissionAcceptedResponse> |> Ok
                | HttpStatusCode.Conflict -> json |> Duplicate |> Error
                | HttpStatusCode.BadRequest ->
                    try
                        let jsonBody = JsonDocument.Parse(json)
                        // I'm not proud of this
                        match ((jsonBody.RootElement.GetProperty("details"))[0]).GetProperty("target").GetString() with
                        | "resourceId" -> json |> BadResourceId |> Error
                        | "effectiveStartTime" -> json |> InvalidEffectiveStartTime |> Error
                        | _ -> json |> CommunicationsProblem |> Error
                    with 
                    | _ -> json |> CommunicationsProblem |> Error
                | _ -> json |> CommunicationsProblem |> Error
            
            return { Payload = usage; Result = result; Headers = azureHeader }
        }
    
    let submitUsageCsharp : Func<MeteringConfigurationProvider, MeteringAPIUsageEventDefinition, Task<MarketplaceSubmissionResult>> = submitUsage
