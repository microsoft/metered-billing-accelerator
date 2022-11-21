namespace PublisherPortal.ViewModels.Meter;

using System.Collections.Generic;

public record DimensionTotalModel(
    string  DimensionName,
    decimal ConsumedDimensionQuantity,
    decimal IncludedDimensionQuantity,
    decimal ToBeProcess);

public record MeterSummaryModel(
    string SubscriptionId,
    string DimensionName,
    decimal ConsumedDimensionQuantity,
    decimal IncludedDimensionQuantity);

public record ToBeReportedModel(
    string SubscriptionId,
    decimal Quantity,
    string PlanId,
    string DimensionName,
    string EffectiveStartTime);

public record PublisherMetersModel(
   string LastProcessedMessage,
   List<DimensionTotalModel> CurrentTotal,
   List<ToBeReportedModel> CurrentToBeReported,
   List<MeterSummaryModel> CurrentMeterSummary);
