using Metering.BaseTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PublisherPortal.ViewModels.Meter
{
    public class MeterSummaryModel
    {
        public string SubscriptionId { get; set; }
        public string DimensionName { get; set; }
        public decimal ConsumedDimensionQuantity { get; set; }
        public decimal IncludedDimensionQuantity { get; set; }

    }
}
