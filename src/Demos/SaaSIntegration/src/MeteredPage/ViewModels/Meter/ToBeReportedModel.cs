using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MeteredPage.ViewModels.Meter
{
    public class ToBeReportedModel
    {
        public string SubscriptionId { get; set; }

        public string Quantity { get; set; }
        public string PlanId { get; set; }

        public string DimensionId { get; set; }

        public string EffectiveStartTime { get; set; }
    }
}
