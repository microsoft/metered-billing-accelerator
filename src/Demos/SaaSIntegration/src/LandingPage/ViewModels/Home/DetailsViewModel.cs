namespace LandingPage.ViewModels.Home;

using Microsoft.Graph;
using Microsoft.Marketplace.SaaS.Models;
using System.Collections.Generic;
using System.Security.Claims;
using Subscription = Microsoft.Marketplace.SaaS.Models.Subscription;

public class DetailsViewModel
{
    public IEnumerable<Claim> UserClaims { get; internal set; }

    public User GraphUser {  get; internal set; }

    public Subscription Subscription { get; internal set; }
    
    public string PurchaseIdToken { get; internal set; }
    
    public OperationList OperationList { get; internal set; }
    
    public SubscriptionPlans SubscriptionPlans { get; internal set; }
}