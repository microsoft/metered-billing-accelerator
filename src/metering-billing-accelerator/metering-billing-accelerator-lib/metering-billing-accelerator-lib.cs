using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Producer;
using MeteringAcceleratorLib.model;

namespace MeteringAcceleratorLib
{
    public class MeteringAcceleratorLib
    {
        private string connectionString;
        private string eventHubName;
        private EventHubProducerClient producer;

        /// <summary>
        /// CTOR
        /// </summary>
        /// <param name="connectionString"></param>
        /// <param name="eventHubName"></param>
        public MeteringAcceleratorLib(string connectionString, string eventHubName = "meter-billing-accelerator")
        {
            this.connectionString = connectionString;
            this.eventHubName = eventHubName;

            this.producer = new EventHubProducerClient(connectionString, eventHubName);
        }

        /// <summary>
        /// Emit a batch of MeterRecords to the Meter Billing Server Queue
        /// </summary>
        /// <param name="meterRecordArray"></param>
        /// <returns></returns>
        public async Task EmitMeterBatchAsync(List<MeterRecord> meterRecordArray)
        {
            using EventDataBatch eventBatch = await this.producer.CreateBatchAsync();
            foreach (MeterRecord meterRecord in meterRecordArray)
            {
                eventBatch.TryAdd(
                    new EventData(
                        new BinaryData(
                             meterRecord
                            )));
            }

            await producer.SendAsync(eventBatch);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="marketplaceSubsriptionId"></param>
        /// <param name="planId"></param>
        /// <param name="dimensionId"></param>
        /// <param name="units"></param>
        /// <param name="tags"></param>
        /// <returns></returns>        
        public async Task EmitAsync(string marketplaceSubsriptionId, string planId, string dimensionId, int units, string tags)
        {
            using EventDataBatch eventBatch = await this.producer.CreateBatchAsync();
            eventBatch.TryAdd(
                new EventData(
                    new BinaryData(
                        new MeterRecord()
                        {
                            marketplaceSubsriptionId = marketplaceSubsriptionId,
                            planId = planId,
                            dimensionId = dimensionId,
                            units = units,
                            tags = tags
                        }
                        )));

            await producer.SendAsync(eventBatch);
        }

    }
}
