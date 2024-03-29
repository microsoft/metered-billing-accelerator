﻿namespace LandingPage.ViewModels.Home;

using Microsoft.Marketplace.SaaS.Models;

public class IndexViewModel
{
    public string DisplayName { get; set; }
    public string Email { get; set; }
    public string SubscriptionName { get; set; }
    public SubscriptionStatusEnum? FulfillmentStatus { get; set; }
    public string PlanName {  get; set; }
    public string SubscriptionId { get; set; }
    public string TenantId { get; set; }
    public string PurchaseIdToken { get; set; }
}