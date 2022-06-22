# Architect Examples Deployment Checklist:
For ARM Template steps are in the following order
1. Create Resource Group 
2. **If Needed** Create Custom Network Security Group **NSG** if default NSG is not sufficent for your deployment. 
3. Create Network and defined the subnets ranges and assign default or custom NSG
    - Hight recommended to defined specific subnet where all private links will use   
4. Create LogAnalytics
5. Create Application Insights
6. Create Private DNS Zone for all private link to use during the deployment configuration
7. Create Key Vault
8. Enable Key Vault VNET Integration
9. Enable Key Vault Private link
10. Enable Key Vault diagnostic and metric Data to Log Analytics

17. Create Azure Storage Account
17. Create Azure Storage Blob Storage
19. Enable Selected Network and select the Custom Network
20. Enable Private link
26. Enable diagnostic and metric Data to Log Analytics

17. Create Azure Event Hubs NameSpaces
17. Create Azure Event Hub
17. Create Azure Consumer group
30. Enable SaaS Portal Web App VNET Integration
20. Enable Private link
26. Enable diagnostic and metric Data to Log Analytics


25. Create Metered Billing App Service Plan 
26. Enable diagnostic and metric Data to Log Analytics
27. Create Metered Billing Web App from "FunctionApp" Kind and reference metered billing App ServicePlan 

28. Link Azure function App Cofiguration to KeyVault
29. Add VNET Inegration Configuration please refer to [docs](https://docs.microsoft.com/en-us/azure/app-service/overview-vnet-integration)
30. Enable Azure function App VNET Integration
31. Enable Azure function App Private link
32. Enable Azure function App diagnostic and metric Data to Log Analytics
33. Enable Azure function App Application Insights

43. Create Application Gateway
44. Configure Front-End IP pool
45. Configure Backend and reference to Web App
46. Configure HTTP Route
47. Configure Listener
48. Enable Application Gateway VNET Integration
49. Enable Application Gateway Private link
50. Enable Application Gateway Application Insight







