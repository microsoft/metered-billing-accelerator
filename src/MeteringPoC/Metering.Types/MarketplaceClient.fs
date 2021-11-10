﻿namespace Metering.Types

open System.Net
open System.Net.Http
open System.Text
open System.Text.Json
open System.Threading.Tasks

module MarketplaceClient =
    let submit (config: MeteringConfigurationProvider) (usage: MeteringAPIUsageEventDefinition) : Task<MarketplaceSubmissionResult> = 
        task {
            let! client = InstanceMetadataClient.createMarketplaceClient config.MeteringAPICredentials
            let json = usage |> Json.toStr

            let meteringApiVersion = "2018-08-31"
            let request = new HttpRequestMessage(HttpMethod.Post, $"/api/usageEvent?api-version={meteringApiVersion}")
            request.Content <- new StringContent(json, Encoding.UTF8, "application/json")

            let! response = client.SendAsync(request)
            let! json = response.Content.ReadAsStringAsync()
            
            let result =
                match response.StatusCode with
                | HttpStatusCode.OK -> 
                    printf "CCC %s" json
                    json |> Json.fromStr<MarketplaceSubmissionAcceptedResponse> |> Ok
                | HttpStatusCode.Conflict -> Duplicate |> Error
                | HttpStatusCode.BadRequest ->
                    try
                        let jsonBody = JsonDocument.Parse(json)
                        // I'm not proud of this
                        match ((jsonBody.RootElement.GetProperty("details"))[0]).GetProperty("target").GetString() with
                        | "resourceId" -> BadResourceId |> Error
                        | "effectiveStartTime" -> InvalidEffectiveStartTime |> Error
                        | _ -> json |> CommunicationsProblem |> Error
                    with 
                    | _ -> json |> CommunicationsProblem |> Error
                | _ -> json |> CommunicationsProblem |> Error
            
            return { Payload = usage; Result = result }
        }