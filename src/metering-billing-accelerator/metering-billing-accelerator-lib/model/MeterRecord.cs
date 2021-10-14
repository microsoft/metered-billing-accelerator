using System;
using System.Collections.Generic;
using System.Text;

namespace MeteringAcceleratorLib.model
{
    /// <summary>
    /// This is the model to submit records to the event hub to be process an emited to the Azure Marketplace
    /// </summary>
    public class MeterRecord
    {
        public string marketplaceSubsriptionId;
        public string planId;
        public string dimensionId;
        public int units;
        public string tags;
    }
}
