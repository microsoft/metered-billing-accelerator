using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace metering_billing_accelerator_web
{
    public class MeteringService : IMeteringService
    {
        private readonly MeteringAcceleratorLib.MeteringAcceleratorLib meterLib;

        /// <summary>
        /// CTRO 
        /// Requires the Event Hub connection string
        /// </summary>
        /// <param name="connectionString"></param>
        public MeteringService(string connectionString)
        {
            if (string.IsNullOrEmpty(connectionString)) //verify there is a connection string
            {
                Console.Error.WriteLine("Can't find EVENT_HUB_CONNECTION_STRING environmantal variable.");
                Environment.Exit(-1);
            }

            Debug.WriteLine("Setting up emitter...");
            this.meterLib = new MeteringAcceleratorLib.MeteringAcceleratorLib(connectionString);  // This uses the defail EventHub name: meter-billing-accelerator      

        }

        /// <summary>
        /// Emit a single meter event
        /// </summary>
        /// <param name="subscriptionId"></param>
        /// <param name="planId"></param>
        /// <param name="dimensionId"></param>
        /// <param name="units"></param>
        /// <param name="tags"></param>
        /// <returns></returns>
        public async Task EmitMeterAsync(string subscriptionId, string planId, string dimensionId, int units, string tags) 
        {
            try // emit one single record.
            {
                await this.meterLib.EmitAsync(subscriptionId, planId, dimensionId, units, tags);
                Console.WriteLine("One meter record submitted.");
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("Possible error the EventHub name doesnt exist.  Default is: meter-billing-accelerator");
                Console.Error.WriteLine(e);
            }
        }
    }
}