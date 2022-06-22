namespace MeteredPage.ViewModels.Meter;

using System.Collections.Generic;

public class CustomerMetersModel
{
    public string SubscriptionId { get; set; }

    public string LastProcessedMessage { get; set; }

    public List<ToBeReportedModel> CurrentToBeReported { get; set; } = new();

    public List<MeterSummaryModel> CurrentMeterSummary { get; set; } = new();
}