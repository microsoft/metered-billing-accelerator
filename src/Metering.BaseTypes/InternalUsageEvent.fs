// Licensed under the MIT license.

namespace Metering.BaseTypes

/// Internal usage event Message, sent from app to aggregator. 
type InternalUsageEvent =
    { /// The resource ID, i.e. SaaS subscription ID or managed app ID.
      MarketplaceResourceId: MarketplaceResourceId
      
      /// Timestamp (wallclock) of the sending app. This is only for recording purposes. The business logic uses EventHub timestamps.
      Timestamp: MeteringDateTime
      
      /// Application-internal name of the meter / billing dimension. 
      MeterName: ApplicationInternalMeterName

      /// The consumed quantity.
      Quantity: Quantity

      /// An optional collection of additional properties.
      Properties: Map<string, string> option }

    override this.ToString() =
        $"{this.Timestamp |> MeteringDateTime.toStr}: InternalUsageEvent {this.MarketplaceResourceId.ToString()} {this.MeterName.value}={this.Quantity.AsFloat}"
