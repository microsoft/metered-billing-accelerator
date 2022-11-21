namespace MeteredPage.ViewModels;

using System.Collections.Generic;

public record CustomerMetersModel(
    string SubscriptionId,
    string LastProcessedMessage,
    List<ToBeReportedModel> CurrentToBeReported,
    List<MeterSummaryModel> CurrentMeterSummary);

public record ToBeReportedModel(
    string SubscriptionId,
    string Quantity,
    string PlanId,
    string DimensionId,
    string EffectiveStartTime);


public record MeterSummaryModel(
    string SubscriptionId,
    string DimensionName,
    string ConsumedDimensionQuantity,
    string IncludedDimensionQuantity);