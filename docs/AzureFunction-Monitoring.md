# Azure Function monitoring on Azure

**Application Insights** monitors running Azure function. It tells publisher about failures and performance issues, and helps publisher to analyze how customers use the thier application.

It's essential to monitor a modern application while it is running. Most importantly, publisher wants to detect failures before most of his customers do. 

Also publisher want to discover and fix performance issues that, while not catastrophic, perhaps slow things down or cause some inconvenience to their users. 

publisher can set up [availability tests](https://docs.microsoft.com/en-us/azure/azure-monitor/app/monitor-web-app-availability) for any HTTP or HTTPS endpoint that's accessible from the public internet. publisherYou don't have to make any changes to the website you're testing. The following steps will show HOW-TO deploy [availability test](https://docs.microsoft.com/en-us/azure/azure-monitor/app/monitor-web-app-availability) for Azure function in **general**. 


### 1- Enable Application Insights for Azure function (in case if it not installed)
If the application Insights feature is not enabled. Publish will enable it as following ![enable AI](../images/monitoring/af/af_1.png)

After turning the feature on, Click on **Enable** then **Apply**
![enable AI](../images/monitoring/af/af_2.png)

![enable AI](../images/monitoring/af/af_2_2.png)


### 2- Access Application Insight dashboard
After the installion complete the Application Insights services will be accessible from the Azure function or resource group.

![enable AI](../images/monitoring/af/af_3.png)

Notice the Availability feature on the left panel.

### 3- Create Availability Test
From Availability Landing page Click **Add Standard (preview) Test** 

![enable AI](../images/monitoring/af/af_4.png)

Fill the test basic information, make sure to enter the URL for the Azure function 
![enable AI](../images/monitoring/af/af_5.png)

**Note**: you can select up to 5 test locations world wide.  From **Test Location** you can pick 5 location base on SaaS geographic target
![enable AI](../images/monitoring/af/af_6.png)


The Availabity test is  created and deployed now and will get a similar dashboard like this
![enable AI](../images/monitoring/af/af_7.png)


### 4- Alerts
Under Application Insight Alerts you can monitor and query different alerts and based on Severity you can edit the alert and change the delivery mechanism 
![enable AI](../images/monitoring/af/af_8.png)


To Edit the Alert for Availability Test. Click on **Alert Rules**

By Default Availability will create an alert for the availability test you created earlier
![enable AI](../images/monitoring/af/af_9.png)

Click on Availability test Alert 
![enable AI](../images/monitoring/af/af_10.png)

Notice the [Action Groups](https://docs.microsoft.com/en-us/azure/azure-monitor/alerts/action-groups) section. Click on action groups
![enable AI](../images/monitoring/af/af_11.png)

Select the Email Group, and fill the alert detail email and click create.

If you need to changethe email of the peron/group to be notified, click on Edit Action Group and edit the email/SMS information
![enable AI](../images/monitoring/af/af_12.png)


For more about the **Action Groups**]** please refer to [Microsoft Docs](https://docs.microsoft.com/en-us/azure/azure-monitor/alerts/action-groups).


### Summary
This section shows How-To 
- Enable Application Insights
- Create Availability Test
- Change Alert to notify group or person.

### Other Monitor Recommendation
- [Event Hub Monitoring and Alert instruction](./Event-Hub-Monitoring.md).
- [App Registration Credentials Monitoring and Alert instruction](./App-Reg-Monitoring.md).
