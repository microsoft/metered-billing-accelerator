namespace PublisherPortal.ViewModels.Meter
{
    public class DimensionTotalModel
    {
        public string DimensionName { get; set; }
        public decimal ConsumedDimensionQuantity { get; set; }
        public decimal IncludedDimensionQuantity { get; set; }
        public decimal ToBeProcess { get; set; }
    }
}
