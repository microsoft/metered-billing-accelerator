# Simplifying metered billing for ISVs on Azure (September 2021)

- Project page: [Azure Marketplace "Metered Billing" Accelerator](https://garagehackbox.azurewebsites.net/hackathons/2356/projects/105718)

`microsoft/metered-billing-accelerator`



> This note describes a development idea: an accelerator solution, to simplify and improve the "metered billing" story for ISVs on the Azure platform, specifically those building 'managed applications' and SaaS offerings.

## Introduction

The Azure Marketplace offers [metered billing APIs][marketplace-metering-service-apis], both for [SaaS][saas-metered-billing] offerings, as well as [managed applications][azure-app-metered-billing]. *Metered billing* allows an ISV to define custom metering dimensions for offers in the Azure Marketplace, measure the customer's consumption around these metering dimensions, submit the metered values to Microsoft, so we can charge our ISV's end customers. However, Microsoft defined requirements for the reporting which force all ISVs to build / re-invent certain common functionalities. 

As a motivating scenario, imagine an ISV charges their end customer around the number of sent e-mails (or any other dimension which involves large numbers of 'consumed' widgets). For example, let's assume the ISV's offer has a monthly price, which includes a certain amount of e-mails sent; the offer could cost 10,- EUR per month and include 1000 e-mails. For each additional 1000 e-mails, the ISV charges 5,- EUR... 

This scenario poses a few challenges for the ISV...

- "Dual  deployment"
- 

## ISV pain points

### What's a 'month', actually?

The first 1000 e-mails are included in the monthly price, for which the ISV already paid. The ISV must track what time range constitutes 'a month', as some customers might purchase and activate the offer on the 1st of January (perfectly aligning billing with the calendar month), while others activate a purchase on the 20th of June, so that the offer's first month ranges from June-20 till July-19. ([docs][marketplace-metering-service-apis-faq])

### Only report the overage to Microsoft

The next complexity stems from the fact that the ISV only should report the overage to Microsoft via custom meters; if a customer only sends 935 e-mails in a given month, these are all included in the monthly fee, and therefore must not be reported to Microsoft. However, if a customer sends 12345 e-mails in a given reporting month, the customer should be charged for the overage of 12345-1000 == 11345 e-mails. 

As a result, the ISV must track which consumption is included in the base price, and which consumption counts as overage, and therefore needs to be reported.

### Aggregation and accumulation of consumed units

If you imagine an offer like bulk e-mail sending, you somehow need to account for many consumption-related events. However, Microsoft designed the metering API to prevent that an ISV overwhelms it with too many calls. Our documentation states *"You (the ISV) should accumulate all units consumed in an hour and then emit it in a single event."* So as an ISV, you need to count and accumulate the consumed units per customer and hour, and at most call our API once per hour and per customer.

Our API support two reporting modes for metered billing, 'single usage events' and a 'batch usage events': 

- The 'single usage event' API call reports the usage in a given hour during the day. "Between 09:00 AM UTC and 09:59 AM UTC, customer XYZ has consumed 2,396000 units of the 1000 e-mails dimension, i.e. they've sent 2396 e-mails"
- The 'batch usage event' API call can contain all consumption for a whole day (actually even 25 hours)... 

### Submit your reports in a timely fashion, or lose money

Our docs indicate that "usage events can only be emitted for the past 24 hours", so you as an ISV better make sure you have a damn reliable cron job and error reporting logic in place, so that you report your customer's consumption in due time, otherwise our API might reject your meters.

### Audit information... 

#### ...for dispute resolution

Once the ISV submits the usage event meter values to Microsoft, it's Microsoft's job to invoice the end customer, and get the money to the ISV. But even in the best of all worlds, there sometimes will be disputes, where customers complain that they "didn't have the 13th beer, actually". So better have some record on who did what when. 

As an ISV, you might like the idea of keeping the actual raw input material for the reporting around for some time, so that you can provide more evidence in conflict situations. *"Hey, last Tuesday, the VM with that IP address in your environment dispatched 1.2 million e-mails, maybe you should have a look..."*

#### ...for churn analysis, business metric, product improvements, and whatever your MBAs dream up

Your business development team's data scientists might like the idea to gain further insights around how your products are being used (assuming you have the right according to your contract with your end customer, and privacy regulation, and whatnot). Having past usage information, across individual customers, or customer cohorts, might be a tremendous treasure trove for your BI team, or your development team.

###  Consumption run rate visibility to the end customer

Aggregating all usage/metering in the ISV side could also provide the customer with real-time visibility regarding their current consumption. For example, a customer could see that for the current month, they still have 200 of their 1000 free e-mails available. In a SaaS care, that UX certainly would be built directly into the SaaS. In the managed app case, the ISV should offer an API to the managed app to retrieve that information, so that the managed app can locally visualize the consumption.

### Metadata augmentation of usage events

The Azure invoice certainly tells you how many widgets you consumed, but no further data. On the other hand, for Azure-own resources you can drill in how much a certain virtual machine costed. In our case, we want to allow the ISV to attach additional metadata inside their code to the usage events. For example, a machine learning solution might add the name of the submitter of a new model training to the event; in a bulk-e-mail scenario, you might want to add a campaign identifier to the events, so that the end-customer can understand how much usage happened per campaign. 

## What's the plan?

Looking at the previously mentioned pain points, these certainly will pop up with many of our ISVs going forward. Full disclosure, until now, I (Christian) haven't had many ISVs actually using custom meters anyhow, so I haven't been able to validate my hypothesis. However, looking at our metering APIs, it seems clear to me that the previously mentioned points will become an issue, once ISVs start building more Azure Marketplace offers.

### When to build 'it'?

The upcoming Hackathon (2021-OCT-12--14) looks like a good opportunity to have multiple interested FTA ISV folks kick-starting such an accelerator solution.

### What shape and form?

It could be a stand-alone code base, or integrated into Mona, many options

### Properties

- Secure (obviously)
- needs to support managed apps, i.e. code running in customer subscriptions must securely 'call home' to the ISV's backend and report usage
- Maybe worthwhile to keep it extensible, so that other environments (GCP, AWS) can be integrated
- Modular software architecture, interface with Azure proprietary APIs over proper clean interfaces
- Functional core, microservices, event sourcing, CQRS, all the good stuff

## Requirements and features

- Unified approach for managed apps and SaaS: In both cases, the usage data should be aggregated and processed by the ISV's own backend. 
  - Even though a managed app might be able to submit metering information to Microsoft's API directly (Arsen mentioned that), it would mean that the ISV doesn't know what's going on.
- Push versus Pull
  - In the manage app case, there are two possible patterns: The managed app could provide an API where the ISV backend pulls the data, but that could accidentally expose endpoints in the customer's subscription... Better to keep the customer environments as closed as possible, and have the ISV expose a single API, where all customer deployments push their metering information to. 
- Support large event volumes
  - Imagine two scenarios, a high- and a low-cost one: A high-cost scenario could be a compute-intense machine learning model training, or video rendering; in that situation, it is no question that each billable event ("video rendered") justifies a metering event to the ISV. On the other hand, a low-margin / high-volume event (like e-mail sent) might be OK to already perform some aggregation within the managed app and only send a "8 e-mails sent"-event.  However, this might increase implementation complexity, so that the ISV might still prefer one event recording API call per low-cost event, which means we have quite an amount of incoming calls.
- Secure cross-tenant interaction, avoid credentials where we can. Ideally, the invocation of the ISV's metering API should happen using a managed identity from the customer tenant, i.e. we have cross-tenant calls using managed identity. Jasper had a good example how to get this going.
- Support multiple managed apps per customer. A managed app might be deployed multiple times, and the backend needs to be able to distinguish between those.

## Compete information (Arsen)

- AWS SaaS Factory team has an example showing how to do billing using Stripe (surprising not their own marketplace metered billing – they often do this since AWS marketplace and SaaS Factory teams don’t seem to be aligned well) 
  - They use DynamoDB table as the aggregation destination for the detailed events prior to them being sent to Stripe [Blog](https://aws.amazon.com/blogs/apn/building-a-third-party-saas-metering-and-billing-integration-on-aws/), [code](https://github.com/aws-samples/aws-saas-factory-billing-and-metering-reference-implementation)
- AWS SaaS Boost also implements Stripe metered billing and uses DynamoDB table as aggregation (since same SaaS Factory team created it)
  - The app developer needs to instrument the app and publish their metering events via AWS EventBidge ([dev guide](https://github.com/awslabs/aws-saas-boost/blob/main/docs/developer-guide.md#billing-integration), [user guide](https://github.com/awslabs/aws-saas-boost/blob/main/docs/user-guide.md#metering-instrumentation))
  - The aggregation happens via Lambda process DynamoDB table as [aggregation endpoint](https://github.com/awslabs/aws-saas-boost/blob/main/metering-billing/lambdas/src/main/java/com/amazon/aws/partners/saasfactory/metering/aggregation/BillingEventAggregation.java)
- AWS Marketplace Samples ([SaaS](https://github.com/aws-samples/aws-marketplace-serverless-saas-integration#metering-for-usage), [Containers](https://github.com/aws-samples/aws-marketplace-metered-container-product#solution-overview)) showing how they do aggregation using DynamoDB
- [AWS Marketplace metering examples in Python](https://docs.aws.amazon.com/marketplace/latest/userguide/saas-code-examples.html) show just the simple calls to the API not the aggregation logic that needs to be implemented by the ISV 

## TODO

- [ ] Check with Julio Colon and Patrick Butler Monterde

## Links

[marketplace-metering-service-apis]: https://docs.microsoft.com/en-us/azure/marketplace/marketplace-metering-service-apis	"Marketplace metered billing APIs"

[marketplace-metering-service-apis-faq]: https://docs.microsoft.com/en-us/azure/marketplace/marketplace-metering-service-apis-faq "Marketplace metered billing APIs - FAQ"

[saas-metered-billing]: https://docs.microsoft.com/en-us/azure/marketplace/partner-center-portal/saas-metered-billing "Metered billing for SaaS offers using the Microsoft commercial marketplace metering service"

[azure-app-metered-billing]: https://docs.microsoft.com/en-us/azure/marketplace/azure-app-metered-billing "Metered billing for managed applications using the marketplace metering service"

[github-commercial-marketplace-managed-application-metering-samples]: https://github.com/microsoft/commercial-marketplace-managed-application-metering-samples "Github sample for Posting custom meters for a Commercial Marketplace managed app offer"

