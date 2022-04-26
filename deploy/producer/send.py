import asyncio
from azure.eventhub.aio import EventHubProducerClient
from azure.eventhub import EventData

async def init():
    # Create a producer client to send messages to the event hub.
    # Specify a connection string to your event hubs namespace and
    # the event hub name.
    producer = EventHubProducerClient.from_connection_string(conn_str="Endpoint=sb://marketplace-saas-metering-standard.servicebus.windows.net/;SharedAccessKeyName=Producer;SharedAccessKey=Ylviql7sH6Fbyr/i+35acRqqF5F+bhKIJ4NizxrKmN8=;EntityPath=2022-04-11-mgjoshevski", eventhub_name="2022-04-11-mgjoshevski")
    async with producer:
        # Create a batch.
        event_data_batch = await producer.create_batch()

        # Add events to the batch.

    
        event_data_batch.add(EventData("""{
                                            "type":"SubscriptionPurchased",
                                            "value":{
                                                "subscription":{
                                                "scope":"6787b636-d6a2-4e30-d671-f7ca570507ef",
                                                "subscriptionStart":"2022-04-11T16:12:26Z",
                                                "renewalInterval":"Monthly",
                                                "plan":{
                                                    "planId":"gold_plan_id",
                                                    "billingDimensions": {
                                                    "user_id":    { "monthly": 100, "annually": 0 },
                                                
                                                    }
                                                }
                                                },
                                                "metersMapping":{
                                                "user_id": "user_id"
                                                }
                                            }
                                            } """))
        


        # Send the batch of events to the event hub.
        await producer.send_batch(event_data_batch)


async def send_metering():
    # Create a producer client to send messages to the event hub.
    # Specify a connection string to your event hubs namespace and
    # the event hub name.
    producer = EventHubProducerClient.from_connection_string(conn_str="Endpoint=sb://marketplace-saas-metering-standard.servicebus.windows.net/;SharedAccessKeyName=Producer;SharedAccessKey=Ylviql7sH6Fbyr/i+35acRqqF5F+bhKIJ4NizxrKmN8=;EntityPath=2022-04-11-mgjoshevski", eventhub_name="2022-04-11-mgjoshevski")
    async with producer:
        # Create a batch.
        event_data_batch = await producer.create_batch()

        # Add events to the batch.
        i = 1
        while i < 1000:

            event_data_batch.add(EventData("""{
                                                "type": "UsageReported",
                                                "value": {
                                                    "internalResourceId": "6787b636-d6a2-4e30-d671-f7ca570507ef",
                                                    "timestamp":          "2022-01-27T09:57:29Z",
                                                    "meterName":          "user_id",
                                                    "quantity":           12
                                                }
                                            } """))
            i += 1


        # Send the batch of events to the event hub.
        await producer.send_batch(event_data_batch)


loop = asyncio.get_event_loop()
#loop.run_until_complete(init())
loop.run_until_complete(send_metering())