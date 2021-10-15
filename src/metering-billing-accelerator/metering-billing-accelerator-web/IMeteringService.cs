using System.Threading.Tasks;

namespace metering_billing_accelerator_web
{
    internal interface IMeteringService
    {
        public Task EmitMeterAsync(string subscriptionId, string planId, string dimensionId, int units, string tags);
    }
}