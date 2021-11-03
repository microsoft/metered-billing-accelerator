namespace Metering.Types

open System.Threading.Tasks

/// Unique identifier of the resource against which usage is emitted. 
type ResourceID = // https://docs.microsoft.com/en-us/azure/marketplace/marketplace-metering-service-apis#metered-billing-single-usage-event
    /// For Azure Application Managed Apps plans, the resourceId is the Managed App resource group Id. 
    | ManagedAppResourceGroupID of ManagedAppResourceGroupID
    
    /// For SaaS offers, the resourceId is the SaaS subscription ID. 
    | SaaSSubscriptionID of SaaSSubscriptionID

module ResourceID =
    let createFromManagedAppResourceGroupID x = x |> ManagedAppResourceGroupID.create |> ManagedAppResourceGroupID

    let createFromSaaSSubscriptionID x = x |> SaaSSubscriptionID.create |> SaaSSubscriptionID

    let convert (st: SubscriptionType) (resolver: DetermineManagedAppResourceGroupID) : Async<ResourceID> =
        match st with
        | ManagedApp -> 
            async {
                let! x = resolver
                
                // curl --request GET --silent -H Metadata:true "http://169.254.169.254/metadata/identity/oauth2/token?api-version=2017-09-01&resource=https%3A%2F%2Fmanagement.core.windows.net%2F"
                //  using var request = new HttpRequestMessage(HttpMethod.Get, $"{config.MSIEndpoint}/?resource={HttpUtility.UrlEncode(resource)}&api-version=2017-09-01");
                //  armHttpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {armToken}");
                return 
                    x
                    |> ManagedAppResourceGroupID.value
                    |> createFromManagedAppResourceGroupID
            }

        | SaaSSubscription s -> 
            let resourceId = 
                s
                |> SaaSSubscriptionID.value
                |> createFromSaaSSubscriptionID 

            async { return resourceId }

type SubscriptionIdToResourceIDConverter = SubscriptionType -> ResourceID
    