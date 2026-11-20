using './main.bicep'

// Applied by hand (see README.md): az deployment group create -g PoTraffic -f infra/main.bicep -p infra/main.bicepparam
param webAppName = 'potraffic-api-win2'
param location = 'westus2'
param appServicePlanId = '/subscriptions/14d963a2-badc-4a1f-9900-553bf01f8de2/resourceGroups/PoShared/providers/Microsoft.Web/serverfarms/asp-poissues-b1-wus2'
param sharedIdentityId = '/subscriptions/14d963a2-badc-4a1f-9900-553bf01f8de2/resourceGroups/PoShared/providers/Microsoft.ManagedIdentity/userAssignedIdentities/mi-poshared-containerapps'
param sharedIdentityClientId = '999ce3e9-7670-4164-9daa-1155e2db432d'
param keyVaultUri = 'https://kv-poshared2.vault.azure.net/'
