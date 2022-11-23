namespace MeteredPage.ViewModels;

using System.Collections.Generic;

//public record ToBeReportedModel(
//    string SubscriptionId,
//    string Quantity,
//    string PlanId,
//    string DimensionId,
//    string EffectiveStartTime);

public record MeterSummaryModel(
    string Name,
    string LastUpdate,
    string ConsumedDimensionQuantity,
    string IncludedDimensionQuantity); 

public record CustomerMetersModel(
    string SubscriptionId,
    List<MeterSummaryModel> CurrentMeterSummary);
