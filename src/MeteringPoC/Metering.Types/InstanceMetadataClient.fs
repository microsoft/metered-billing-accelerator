namespace Metering.Types

open System
open System.Net.Http
open System.Net.Http.Json
open System.Web
open Microsoft.FSharp.Control
open System.Threading.Tasks

//type ResourceGroupDefinition = { ManagedBy: string }

module InstanceMetadataClient = 
    type TokenResponse = { access_token: string }
    
    let clientWithMetadataTrue endpoint =
        let client = new HttpClient()
        client.BaseAddress <- new Uri(endpoint)
        client.DefaultRequestHeaders.Add("Metadata", "true")
        client

    let get_access_token : (string -> Task<string>) =
        let getMSIConfigFromEnvironment =
            let env s = 
                match s |> Environment.GetEnvironmentVariable with
                | v when String.IsNullOrWhiteSpace(v) -> None
                | v -> Some v

            // https://docs.microsoft.com/en-us/azure/app-service/overview-managed-identity?tabs=dotnet#using-the-rest-protocol
            match (env "IDENTITY_ENDPOINT", env "IDENTITY_HEADER", env "MSI_ENDPOINT", env "MSI_SECRET") with
            | (None, None, Some msiEndpoint, Some msiSecret) -> (msiEndpoint, Some ("secret", msiSecret), "2017-09-01")
            | (Some identityEndpoint, Some identityHeader, _, _) -> (identityEndpoint, Some ("X-IDENTITY-HEADER", identityHeader), "2019-08-01")
            | _ -> ("http://169.254.169.254/metadata/identity/oauth2/token", None, "2019-08-01")
            
        let access_token_retriever (resource: string) : Task<string> =
            task {
                let (endpoint, headerAndSecret, apiVersion) = getMSIConfigFromEnvironment
    
                use client = clientWithMetadataTrue endpoint

                match headerAndSecret with
                | Some (h, v) -> client.DefaultRequestHeaders.Add(h, v) 
                | None -> ()
    
                let! { access_token = token } =
                    client.GetFromJsonAsync<TokenResponse>($"?resource={resource |> HttpUtility.UrlEncode}&api-version={apiVersion}")

                return token
            }
        
        access_token_retriever

    let createWithAccessToken (resource: string) : Task<HttpClient> =
        task {
            let! access_token = get_access_token resource
            let client = new HttpClient()
            client.BaseAddress <- new Uri(resource)
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {access_token}")
            return client
        }

    //let private demo =
    //    let inspect header a =
    //        if String.IsNullOrEmpty header 
    //        then printfn "%s" a
    //        else printfn "%s: %s" header a
    //        a

    //    task {
    //        let! access_token = 
    //            "https://management.azure.com/" 
    //            |> get_access_token 

    //        access_token
    //        |> inspect "access_token"
    //        |> ignore
    //    }
    //    |> Async.AwaitTask
    //    |> Async.RunSynchronously

    //    task {
    //        let! client = 
    //            "https://management.azure.com/"
    //            |> create 

    //        client
    //        |> (fun (client : HttpClient) -> 
    //            task {
    //                return! client.GetStringAsync("/subscriptions?api-version=2020-01-01")
    //            }
    //            |> Async.AwaitTask
    //            |> Async.RunSynchronously)
    //        |> inspect "subscriptions"
    //        |> ignore
    //    }
    //    |> Async.AwaitTask
    //    |> Async.RunSynchronously
