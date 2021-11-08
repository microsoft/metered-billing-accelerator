namespace Metering.Types

open System.Threading.Tasks

module MarketplaceClient =
    let submit (config: MeteringConfigurationProvider) (usage: MeteringAPIUsageEventDefinition) : Task<MarketplaceSubmissionResult> = 
        config.SubmitMeteringAPIUsageEvent 
        failwith "notdone"