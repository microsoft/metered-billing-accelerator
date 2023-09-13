# How to handle subscription updates

When a subscription gets updated with a new plan, some things stay the same, some will be different.

The following properties **don't** change:

- `Subscription.MarketplaceResourceId`
- `Subscription.RenewalInterval`
- `Subscription.SubscriptionStart`

Only the `Subscription.Plan` changes, in particular `Plan.PlanId` and `Plan.BillingDimensions`.

The following table shows what happens when an existing dimension is removed, or newly introduced:

| Dimension in old plan                      | Dimension in new plan                  | Effect                                                       |
| ------------------------------------------ | -------------------------------------- | ------------------------------------------------------------ |
| 🟢 Dimension is using 'included quantities' | ❌ Doesn't exist any longer             | ❌ Just delete the dimension                                  |
| 🔴 Dimension is in the overage              | ❌ Doesn't exist any longer             | 🚀 Overage must immediately (hour must be forcefully closed)  |
| ❌ Dimension didn't exist before            | ✅ Newly introduced dimension           | ✅ Add it as-is to the plan. Unclear how included quantities should be handled. For example, if somebody changes the plan an the mid of the billing cycle, should 50% of the included quantities already be pre-consumed? |
| 🟢 Dimension is using 'included quantities' | ✅ Newly introduced dimension, but with |                                                              |

## Dimension Migrations

With *migration*, we refer to dimensions that exist under the same name in both the old and the new plan, but might vary in details.

| Dimension in old plan                      | Dimension in new plan                  | Effect                                                       |
| :----------------------------------------- | -------------------------------------- | ------------------------------------------------------------ |
| s | s |s|

🚀⛔🛑❌🚫✅🆕🔄️🔴🟡🟢🟥🟧🟨🟩🟦🟪⬛⬜◼️◻️

