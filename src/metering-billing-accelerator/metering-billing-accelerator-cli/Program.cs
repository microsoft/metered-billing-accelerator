using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MeteringAcceleratorLib;

namespace EHMeterCLI
{
    class Program
    {
        /// <summary>
        /// This is a CLI to test the metering library.
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        static async Task Main(string[] args)
        {
            string connectionString = Environment.GetEnvironmentVariable("EVENT_HUB_CONNECTION_STRING");

            if (string.IsNullOrEmpty(connectionString)) //verify there is a connection string
            {
                await meterLib.EmitAsync("subscription1", "plan1", "dimension1", 1, "");
                Console.WriteLine("One meter record submitted.");
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("Possible error the EventHub name doesnt exist.  Default is: meter-billing-accelerator");
                Console.Error.WriteLine(e);
            }

            //create a set of records 
            List<MeteringAcceleratorLib.model.MeterRecord> meterRecords = new List<MeteringAcceleratorLib.model.MeterRecord>();
            var meterRecord0 = new MeteringAcceleratorLib.model.MeterRecord()
            {
                marketplaceSubsriptionId = "subscription2",
                planId = "plan2",
                dimensionId = "dim1",
                tags = "",
                units = 2
            };
            var meterRecord1 = new MeteringAcceleratorLib.model.MeterRecord()
            {
                marketplaceSubsriptionId = "subscription2",
                planId = "plan2",
                dimensionId = "dim2",
                tags = "",
                units = 30
            };
            meterRecords.Add(meterRecord0);
            meterRecords.Add(meterRecord1);

            try  //emy all records created in previous step.
            {
                await meterLib.EmitMeterBatchAsync(meterRecords);

            }
            catch (Exception e)
            {
                Console.Error.WriteLine("Possible error the EventHub name doesnt exist.  Default is: meter-billing-accelerator");
                Console.Error.WriteLine(e);
            }

        }
    }
}
