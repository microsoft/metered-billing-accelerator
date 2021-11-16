namespace Metering.Types

open System
open System.Collections.Generic
open System.Threading.Tasks
open System.Net.Http
open System.Net.Http.Json
open System.Web
open Microsoft.FSharp.Control

type ServicePrincipalCredential = 
    { clientId : string 
      clientSecret : string
      tenantId : string }

type MeteringAPICredentials =
    | ManagedIdentity
    | ServicePrincipalCredential of ServicePrincipalCredential

module MeteringAPICredentials =
    let createServicePrincipal tenantId clientId clientSecret =
        { clientId = clientId
          clientSecret = clientSecret
          tenantId = tenantId } |> ServicePrincipalCredential

module InstanceMetadataClient = 
    type TokenResponse = { access_token: string }
    
    // When running in managed app, use the managed identity
    // When running in SaaS, use service principal

    let clientWithMetadataTrue endpoint =
        let client = new HttpClient()
        client.BaseAddress <- new Uri(endpoint)
        client.DefaultRequestHeaders.Add("Metadata", "true")
        client

    let private get_access_token : (MeteringAPICredentials -> string -> Task<string>) =
        let getMSIConfigFromEnvironment : (string * (string * string) option * string) =
            let env s = 
                match s |> Environment.GetEnvironmentVariable with
                | v when String.IsNullOrWhiteSpace(v) -> None
                | v -> Some v

            // https://docs.microsoft.com/en-us/azure/app-service/overview-managed-identity?tabs=dotnet#using-the-rest-protocol
            match (env "IDENTITY_ENDPOINT", env "IDENTITY_HEADER", env "MSI_ENDPOINT", env "MSI_SECRET") with
            | (None, None, Some msiEndpoint, Some msiSecret) -> (msiEndpoint, Some ("secret", msiSecret), "2017-09-01")
            | (Some identityEndpoint, Some identityHeader, _, _) -> (identityEndpoint, Some ("X-IDENTITY-HEADER", identityHeader), "2019-08-01")
            | _ -> ("http://169.254.169.254/metadata/identity/oauth2/token", None, "2019-08-01")
            
        let access_token_retriever (cred: MeteringAPICredentials) (resource: string) : Task<string> =
            task { 
                match cred with
                | ManagedIdentity ->
                    let (endpoint, headerAndSecret, apiVersion) = getMSIConfigFromEnvironment
                    use tokenClient = clientWithMetadataTrue endpoint
                    match headerAndSecret with
                    | Some (h, v) -> tokenClient.DefaultRequestHeaders.Add(h, v) 
                    | None -> ()

                    let query = $"?resource={resource |> HttpUtility.UrlEncode}&api-version={apiVersion}"
                    let! { access_token = access_token } =
                        tokenClient.GetFromJsonAsync<TokenResponse>(query)
                        
                    //let client = new HttpClient()
                    //client.DefaultRequestHeaders.Add("Authorization", $"Bearer {access_token}")

                    return access_token
                | ServicePrincipalCredential spc ->
                    let uri = 
                        $"https://login.microsoftonline.com/{spc.tenantId}/oauth2/token"

                    let content = 
                        [ "grant_type", "client_credentials"
                          "client_id", spc.clientId
                          "client_secret", spc.clientSecret 
                          "resource", resource |> HttpUtility.UrlEncode ]
                        |> List.map (fun (x,y) -> new KeyValuePair<string,string>(x,y))
                        |> (fun x -> new FormUrlEncodedContent(x))
                              
                    let request = new HttpRequestMessage(HttpMethod.Post, uri)
                    request.Content <- content
                        
                    let tokenClient = new HttpClient()
                    let! response = tokenClient.SendAsync(request)

                    let! { access_token = access_token } =
                        response.Content.ReadFromJsonAsync<TokenResponse>()

                    return access_token

                    //let! response = tokenClient.GetStringAsync(query)
                    //return 
                    //    response
                    //    |> Decode.fromString (Decode.object (fun get -> get.Required.Field "access_token" Decode.string))
                    //    |> function 
                    //        | Ok access_token -> access_token
                    //        | Error _ -> failwith "Could not find access_token"
            }
                
        access_token_retriever
 
    let private create cred resource uri =
        task {
            let! access_token = get_access_token cred resource
            let client = new HttpClient()
            client.BaseAddress <- new Uri(uri)
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {access_token}")
            return client
        }
        
    let createArmClient () =
        create 
            ManagedIdentity
            "https://management.azure.com/" // resource
            "https://management.azure.com/" // uri

    let createMarketplaceClient cred =
        let documentedEndpoint = "marketplaceapi.microsoft.com"
        let seeminglyNewEndpoint = "saasapi.azure.com"

        // https://docs.microsoft.com/en-us/azure/marketplace/partner-center-portal/pc-saas-registration#get-the-token-with-an-http-post
        create
            cred
            "20e940b3-4c77-4b0b-9a53-9e16a1b010a7" // resource
            $"https://{seeminglyNewEndpoint}/"

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
