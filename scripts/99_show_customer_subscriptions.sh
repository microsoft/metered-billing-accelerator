#!/bin/bash

query='resources
| where type =~ "microsoft.solutions/applications"
| extend provisioningState = properties.provisioningState
| extend managedResourceGroupId = properties.managedResourceGroupId
| extend billing = parse_json(strcat("{\"resourceUri\": \"", id, "\", \"resourceId\": \"", properties.billingDetails.resourceUsageId, "\"}"))
| project managedResourceGroupId, kind, location, provisioningState, plan, billing
'

# List all managed apps of my customers
az graph query -q "${query}" | jq .data

