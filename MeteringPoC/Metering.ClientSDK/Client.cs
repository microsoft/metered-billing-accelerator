namespace Metering.ClientSDK
{
    using System;
    using System.Threading.Tasks;
    using Types;

    public class Client
    {
        public async Task SubmitAsync(string dimension, int unit)
        {
            await Task.Delay(1);
                
            MeteringValue _ = new(
                timestamp: DateTime.UtcNow, 
                dimension: Dimension.NewDimension(dimension), 
                unit: Unit.NewUnit(unit));
        }
    }
}