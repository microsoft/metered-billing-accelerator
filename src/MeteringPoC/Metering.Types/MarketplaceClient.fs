namespace Metering.Types

open System
open System.Net
open System.Net.Http
open System.Text
open System.Text.Json
open System.Threading.Tasks

module MarketplaceClient =

    let private meteringApiVersion = "2018-08-31"

    let submitUsage (config: MeteringConfigurationProvider) (usage: MeteringAPIUsageEventDefinition) : Task<MarketplaceSubmissionResult> = 
        task {
            let! client = InstanceMetadataClient.createMarketplaceClient config.MeteringConnections.MeteringAPICredentials
            let json = usage |> Json.toStr 0
 
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
                | HttpStatusCode.Conflict -> json |> MarketplaceSubmissionError.Duplicate |> Error
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

    /// The batch usage event API allows you to emit usage events for more than one purchased resource at once. 
    /// It also allows you to emit several usage events for the same resource as long as they are for different calendar hours. 
    /// The maximal number of events in a single batch is 25.
    let submitUsages (config: MeteringConfigurationProvider) (usage: BatchMeteringAPIUsageEventDefinition) : Task<MarketplaceSubmissionResult> = 
        // https://docs.microsoft.com/en-us/azure/marketplace/marketplace-metering-service-apis#metered-billing-batch-usage-event
        failwith "not implemented"

    
    let submitUsageCsharp : Func<MeteringConfigurationProvider, MeteringAPIUsageEventDefinition, Task<MarketplaceSubmissionResult>> = submitUsage
