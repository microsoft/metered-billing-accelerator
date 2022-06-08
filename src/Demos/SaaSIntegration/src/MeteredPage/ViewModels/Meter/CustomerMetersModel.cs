using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MeteredPage.ViewModels.Meter
{
    public class CustomerMetersModel
    {
       public string SubscriptionId { get; set; }
       public List<ToBeReportedModel> CurrentToBeReported { get; set; }
       public String lastProcessedMessage { get; set; }
       public List<MeterSummaryModel> CurrentMeterSummary { get; set; }

        public CustomerMetersModel()
        {
            this.CurrentMeterSummary = new List<MeterSummaryModel>();
            this.CurrentToBeReported = new List<ToBeReportedModel>();
        }
    }
}
