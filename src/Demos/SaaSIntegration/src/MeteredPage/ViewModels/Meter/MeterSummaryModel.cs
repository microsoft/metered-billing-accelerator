namespace MeteredPage.ViewModels.Meter;

public class MeterSummaryModel
{
    public string SubscriptionId { get; set; }
    public string DimensionName { get; set; }
    public string ConsumedDimensionQuantity { get; set; }
    public string IncludedDimensionQuantity { get; set; }
}
