using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PublisherPortal.ViewModels.Meter
{
    public class PublisherMetersModel
    {
        public List<DimensionTotalModel> CurrentTotal { get; set; }
        public List<ToBeReportedModel> CurrentToBeReported { get; set; }
       public String lastProcessedMessage { get; set; }
       public List<MeterSummaryModel> CurrentMeterSummary { get; set; }

        public PublisherMetersModel()
        {
            this.CurrentMeterSummary = new List<MeterSummaryModel>();
            this.CurrentToBeReported = new List<ToBeReportedModel>();
            this.CurrentTotal = new List<DimensionTotalModel>();
        }
    }
}
