namespace Metering.Types

open System.IO
open System.Net.Http
open System.Web
open Newtonsoft.Json
open FSharp.Control.Tasks

type TokenDefinition = { access_token: string }
//type ResourceGroupDefinition = { ManagedBy: string }

module InstanceMetadataClient = 
    let demo =
        task {
            let msiEndpoint = "http://169.254.169.254/metadata/identity/oauth2/token"
            let resource = "https://management.core.windows.net/"
            let apiVersion = "2017-09-01"
            let url = $"{msiEndpoint}?resource={HttpUtility.UrlEncode(resource)}&api-version={apiVersion}"

            use client = new HttpClient()
            use request = new HttpRequestMessage(HttpMethod.Get, url)
            request.Headers.Add("Metadata", "true")
            // request.Headers.Add("Secret", config.MSISecret)

            let! response = client.SendAsync(request)

            let! responseBody = response.Content.ReadAsStringAsync()
            //let accessToken = JsonConvert.DeserializeObject<TokenDefinition>(responseBody).access_token

            return responseBody
        }
        |> Async.AwaitTask
        |> Async.RunSynchronously
