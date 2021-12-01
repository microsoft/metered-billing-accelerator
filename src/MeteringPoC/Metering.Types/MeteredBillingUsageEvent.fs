namespace Metering.Types

// https://docs.microsoft.com/en-us/azure/marketplace/marketplace-metering-service-apis#metered-billing-single-usage-event
type MeteredBillingUsageEvent =
    { 
        /// Unique identifier of the resource against which usage is emitted. 
        ResourceID: MarketplaceResourceID 
          
        /// How many units were consumed for the date and hour specified in effectiveStartTime, must be greater than 0, can be integer or float value
        Quantity: Quantity 
          
        /// Custom dimension identifier.
        DimensionId: DimensionId
          
        /// Time in UTC when the usage event occurred, from now and until 24 hours back.
        EffectiveStartTime: MeteringDateTime 
          
        /// ID of the plan purchased for the offer.
        PlanId: PlanId } 

//type MeteredBillingUsageEventBatch =
//    MeteredBillingUsageEvent list
