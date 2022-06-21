using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PublisherPortal.ViewModels.Meter
{
    public class ToBeReportedModel
    {
        public string SubscriptionId { get; set; }

        public decimal Quantity { get; set; }
        public string PlanId { get; set; }

        public string DimensionName { get; set; }

        public string EffectiveStartTime { get; set; }
    }
}
