using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Marketplace.SaaS;
using Microsoft.Marketplace.SaaS.Models;
using PublisherPortal.ViewModels;
using PublisherPortal.ViewModels.Home;
using PublisherPortal.ViewModels.Shared;
using Azure.Messaging.EventHubs.Producer;
using Metering.BaseTypes;
using Metering.ClientSDK;
using Metering.Integration;
using Subscription = Microsoft.Marketplace.SaaS.Models.Subscription;
using Microsoft.FSharp.Collections;

namespace PublisherPortal.Controllers;

[Authorize]
[Route("~/")]
public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly IMarketplaceSaaSClient _marketplaceSaaSClient;


    static async Task<T> readJson<T>(string name) => Metering.BaseTypes.Json.fromStr<T>(name);

    


    public HomeController(
        ILogger<HomeController> logger,
        IMarketplaceSaaSClient marketplaceSaaSClient)
    {
        _logger = logger;
        _marketplaceSaaSClient = marketplaceSaaSClient;
    }

    /// <summary>
    /// Shows a list of all subscriptions
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>IActionResult</returns>
    public async Task<IActionResult> IndexAsync(CancellationToken cancellationToken)
    {
        IList<Subscription> subscriptionsList = new List<Subscription>();
            
        var subscriptions = _marketplaceSaaSClient.Fulfillment.ListSubscriptionsAsync(cancellationToken: cancellationToken);

        await foreach (var subscription in subscriptions)
        {
            subscriptionsList.Add(subscription);
        }
            
        var model = new IndexViewModel()
        {
            Subscriptions = subscriptionsList.OrderBy(s => s.Name).ToList<Subscription>()
        };

        return View(model);
    }

    /// <summary>
    /// Shows subscription details
    /// </summary>
    /// <param name="id">The subscription ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>IActionResult</returns>
    [Route("Subscription/{id}")]
    public async Task<IActionResult> SubscriptionAsync(Guid id, CancellationToken cancellationToken)
    {

        Subscription subscription = (await _marketplaceSaaSClient.Fulfillment.GetSubscriptionAsync(id, cancellationToken: cancellationToken)).Value;
        SubscriptionPlans plans = (await _marketplaceSaaSClient.Fulfillment.ListAvailablePlansAsync(id, cancellationToken: cancellationToken)).Value;

        var model = new SubscriptionViewModel()
        {

            Subscription = subscription,
            Plans = plans.Plans
        };

        return View(model);
    }

    [Route("Privacy")]
    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    [Route("Error")]
    public IActionResult Error(ErrorViewModel model)
    {
        return View(model);
    }

    /// <summary>
    /// This action will mark the subscription State as Subscribed
    /// </summary>
    /// <param name="id">The subscription ID</param>
    /// <param name="planId">The plan ID as defined in Partner Center</param>
    /// <param name="cancellationToken">Cancelltion token</param>
    /// <returns>IActionResult</returns>
    [Route("Activate/{id}/{planId}")]
    public async Task<IActionResult> ActivateAsync(Guid id, string planId, CancellationToken cancellationToken)
    {
        SubscriberPlan subscriberPlan = new SubscriberPlan()
        {
            PlanId = planId,
        };

        var response = await _marketplaceSaaSClient.Fulfillment.ActivateSubscriptionAsync(id, subscriberPlan, cancellationToken: cancellationToken);

        if(response.Status == 200)
        {
            ActiveMeteredSubscription(id, planId);
        }


        return this.RedirectToAction("Subscription", new { id = id });

    }

    /// <summary>
    /// This action will mark the subscription State as Unsubscribed
    /// </summary>
    /// <param name="id">The id of the subscription as a GUID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>IActionResult</returns>
    [Route("Delete/{id}")]
    public async Task<IActionResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        _logger.Log(LogLevel.Information, "Deleting subscription");

        var operationId = await _marketplaceSaaSClient.Fulfillment.DeleteSubscriptionAsync(id, cancellationToken: cancellationToken);
            
        _logger.Log(LogLevel.Information, $"Operation ID: {operationId}");

        return this.RedirectToAction("Subscription", new { id = id });
    }

    /// <summary>
    /// Changes plan for the subscription based on the plan ID
    /// </summary>
    /// <param name="subscriptionId">The subscription ID as a GUID</param>
    /// <param name="planId">The plan ID as assigned in Partner Center</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>IActionResult</returns>
    [Route("ChangePlan/{SubscriptionId}/{planId}")]
    public IActionResult ChangePlan(Guid subscriptionId, string planId, CancellationToken cancellationToken)
    {
        _logger.Log(LogLevel.Information, $"Changing Plan to: {planId}");

        var subscriberPlan = new SubscriberPlan()
        {
            PlanId = planId,
        };

        var operationId = this._marketplaceSaaSClient.Fulfillment.UpdateSubscription(subscriptionId, subscriberPlan, cancellationToken: cancellationToken);

        _logger.Log(LogLevel.Information, $"Operation ID: {operationId}");


        return this.RedirectToAction("Operations", new { subscriptionId = subscriptionId, operationId = operationId });
    }

    /// <summary>
    /// Gets a specific operation for a given subscription
    /// </summary>
    /// <param name="subscriptionId">The subscription ID</param>
    /// <param name="operationId">The operation's ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>IActionResult</returns>
    [Route("Operations/{subscriptionId}/{operationId}")]
    public async Task<IActionResult> OperationsAsync(Guid subscriptionId, Guid operationId, CancellationToken cancellationToken)
    {
        var subscription = (await _marketplaceSaaSClient.Fulfillment.GetSubscriptionAsync(subscriptionId, cancellationToken: cancellationToken)).Value;
        var subscriptionOperations = (await _marketplaceSaaSClient.Operations.ListOperationsAsync(subscriptionId, cancellationToken: cancellationToken)).Value;
        var operationStatus = (await _marketplaceSaaSClient.Operations.GetOperationStatusAsync(subscriptionId, operationId, cancellationToken: cancellationToken)).Value;

        var model = new OperationsViewModel()
        {
            Subscription = subscription,
            SubscriptionOperations = subscriptionOperations.Operations,
            OperationStatus = operationStatus
        };

        return View(model);
    }

    /// <summary>
    /// Changes the plan for a given subscription
    /// </summary>
    /// <param name="subscriptionId">The subscription ID</param>
    /// <param name="planId">The target plan ID as defined in Partner Center</param>
    /// <param name="operationId">The ChangePlan operation ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>IActionResult</returns>
    [Route("Update/{subscriptionId:Guid}/{planId}/{operationId:Guid}")]
    public async Task<IActionResult> UpdateAsync(Guid subscriptionId, string planId, Guid operationId, CancellationToken cancellationToken)
    {
        var updateOperation = new UpdateOperation()
        {
            PlanId = planId,
            Status = UpdateOperationStatusEnum.Success
        };

        var status = (await _marketplaceSaaSClient.Operations.UpdateOperationStatusAsync(subscriptionId,
            operationId, updateOperation, cancellationToken: cancellationToken)).Status;

        return this.RedirectToAction("Subscription", new { id = subscriptionId });
    }



    /// <summary>
    /// Add Metering Subscription in metered Acceleralator 
    /// </summary>
    /// <param name="subscriptionId">The subscription ID</param>
    /// <param name="planId">Plan ID</param>
    /// <returns></returns>
    private async void ActiveMeteredSubscription(Guid? subscriptionId, string planId)
    {
        // Get all plans
        var plans = _marketplaceSaaSClient.Fulfillment.ListAvailablePlans((Guid)subscriptionId).Value.Plans;

        // Create Key Maps for Meter Accelerator Subscription 
        var billingDimensions = new FSharpMap <DimensionId, IncludedQuantitySpecification>(new List<Tuple<DimensionId, IncludedQuantitySpecification>>());
        var dimensionsMapping = new FSharpMap<ApplicationInternalMeterName, DimensionId>(new List<Tuple<ApplicationInternalMeterName, DimensionId >> ());

        // Build Plan now
        foreach (var plan in plans)
        {
            if (plan.PlanId == planId)
            {
                foreach (var billing in plan.PlanComponents.RecurrentBillingTerms)
                {
                    foreach (var dim in billing.MeteredQuantityIncluded)
                    {
                        if (billing.TermUnit.ToString() == "P1M")
                        {
                            billingDimensions.Add(DimensionIdModule.create(dim.DimensionId), new IncludedQuantitySpecification(QuantityModule.create(Convert.ToUInt32(dim.Units)), null));
                        }
                        else
                        {
                            billingDimensions.Add(DimensionIdModule.create(dim.DimensionId), new IncludedQuantitySpecification(null,QuantityModule.create(Convert.ToUInt32(dim.Units))));
                        }
                        // add to Dim list
                        dimensionsMapping.Add(ApplicationInternalMeterNameModule.create(dim.DimensionId), DimensionIdModule.create(dim.DimensionId));
                    }
                }
                break;
            }
        }


        // Call metering SDK now
        var meterplan = new Metering.BaseTypes.Plan(PlanIdModule.create(planId),billingDimensions);
        var meterMapping = InternalMetersMapping.NewInternalMetersMapping(dimensionsMapping);



        System.Threading.CancellationTokenSource cts = new System.Threading.CancellationTokenSource();
        var eventHubProducerClient = MeteringConnectionsModule.createEventHubProducerClientForClientSDK();
        var sub = new SubscriptionCreationInformation(
        internalMetersMapping: meterMapping,
        subscription: new(
            plan: meterplan,
            internalResourceId: InternalResourceIdModule.fromStr(subscriptionId.ToString()),
            renewalInterval: RenewalInterval.Monthly,
            subscriptionStart: MeteringDateTimeModule.now()));

        await eventHubProducerClient.SubmitSubscriptionCreationAsync(sub, cts.Token);
 


    }
}