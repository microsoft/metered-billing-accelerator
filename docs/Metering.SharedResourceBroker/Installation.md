# Installation of the Publisher backend

The script for publishing is using a bash script to provision the backend, as theres no way to create Azure AAD applications, AAD users and groups in ARM/BICEP without using embedded deploymentScripts.  

Theres two scripts in place. Frist one creates the backend, the last one creates the resources which are being shared. [setup.sh](../../scripts/Metering.SharedResourceBroker/setup.sh) contains all steps to setup the backend. Main components are: 

* Create a **resource group** for the publisher backend
* Create AAD **security group**. Group will be assigned the permissions needed for the scenario - like Contributor to an storage account, message queue,  event hub etc.
* **Key Vault** which holds the two secrets, Bootstrap secret used when ARM installs a managed resource and the secret managed applications passes when sending life cycle events.
* **Application service and web application** to host the backend API. 
* **User Assigned managed identity**, used by the web application for getting access to key vault and for creating Service principals.
* Once above is provisioned,  the `Backend` solution can be deployed to the web application created. 
  * A swagger contract is exposed and be be used for testing. Secret can be passed *either* in Authorization header or in the payload. The secret is available in the key vault and name `BootstrapSecret`. If yo![](./img/openapi.png) 

