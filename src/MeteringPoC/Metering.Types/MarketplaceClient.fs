namespace Metering.Types

open System.Threading.Tasks

module MarketplaceClient =
    let submit (config: MeteringConfigurationProvider) (usage: MeteringAPIUsageEventDefinition) : Task<MarketplaceSubmissionResult> = 
        failwith "notdone"