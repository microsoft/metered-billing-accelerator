$postfix="SharedResources"
$groupName="SRB.ManagedApp.Submitters"
$resourceGroupName="SRB.Backend.$($postfix)"
$location="westeurope"
$storageAccountName="storage$($postfix)".ToLower()

$resourceGroup=( az group create `
    --location $location `
    --name $resourceGroupName ) | ConvertFrom-Json

$storageAccount=( az storage account create `
    --location $location `
    --resource-group $resourceGroupName `
    --name $storageAccountName `
    --allow-shared-key-access true `
    --sku Standard_LRS ) | ConvertFrom-Json    

$group = Get-Content -Path .\group_id.txt

az role assignment create `
     --assignee $group `
     --role "Storage Queue Data Contributor" `
     --scope $storageAccount.id

 