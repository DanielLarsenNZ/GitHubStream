$location = 'australiaeast'
$loc = 'aue'
$rg = 'githubstream-rg'
$tags = 'project=GitHubStream', 'owner=dalars'
$webjobsStorage = "githubstream$loc"
$functionApp = "githubstream-$loc-fn"
$insights = 'githubstream-insights'
$eventhubNamespace = 'githubstream-hub'
$eventhubs = 'githubstream'
$eventhubAuthRule = 'SenderListener1'
$servicebusNamespace = 'githubstream-bus'
$topics = 'comments'
$servicebusAuthRule = 'SenderReceiver1'
$servicebusSku = 'Standard'
$eventhubsSku = 'Basic'
$eventhubsRetentionDays = 1
$eventhubsPartitions = 2    # 2 - 32. Cannot be changed after deployment. Good discussion here: https://medium.com/@iizotov/azure-functions-and-event-hubs-optimising-for-throughput-549c7acd2b75

$ErrorActionPreference = 'Stop'

# RESOURCE GROUP
az group create -n $rg --location $location --tags $tags

# EVENT HUBS
# https://docs.microsoft.com/en-us/cli/azure/eventhubs?view=azure-cli-latest

# Create Event Hub, namespace and auth rule
az eventhubs namespace create -g $rg --name $eventhubNamespace --location $location --tags $tags --sku $eventhubsSku

foreach ($eventhub in $eventhubs) {
    az eventhubs eventhub create -g $rg --namespace-name $eventhubNamespace --name $eventhub --message-retention $eventhubsRetentionDays --partition-count $eventhubsPartitions
}

az eventhubs namespace authorization-rule create -g $rg --namespace-name $eventhubNamespace --name $eventhubAuthRule --rights Listen Send

# Get connection string
$eventhubConnectionString = ( az eventhubs namespace authorization-rule keys list --resource-group $rg --namespace-name $eventhubnamespace --name $eventhubauthrule | ConvertFrom-Json ).primaryConnectionString
$eventhubConnectionString

# SERVICE BUS
# https://docs.microsoft.com/en-us/cli/azure/servicebus/namespace?view=azure-cli-latest#az-servicebus-namespace-create

# Create namespace, queue and auth rule
az servicebus namespace create -g $rg --name $servicebusNamespace --location $location --tags $tags --sku $servicebusSku

foreach ($topic in $topics) {
    az servicebus topic create -g $rg --namespace-name $servicebusNamespace --name $topic #--default-message-time-to-live 'P14D'
}

az servicebus namespace authorization-rule create -g $rg --namespace-name $servicebusNamespace --name $servicebusAuthRule --rights Listen Send

# Get connection string
$servicebusConnectionString = ( az servicebus namespace authorization-rule keys list -g $rg --namespace-name $servicebusNamespace --name $servicebusAuthRule | ConvertFrom-Json ).primaryConnectionString
$servicebusConnectionString



# STORAGE ACCOUNT
# https://docs.microsoft.com/en-us/cli/azure/storage/account?view=azure-cli-latest#az-storage-account-create
az storage account create -n $webjobsStorage -g $rg -l $location --tags $tags --sku Standard_LRS

$webjobsStorageConnection = ( az storage account show-connection-string -g $rg -n $webjobsStorage | ConvertFrom-Json ).connectionString
$webjobsStorageConnection

# APPLICATION INSIGHTS
#  https://docs.microsoft.com/en-us/cli/azure/ext/application-insights/monitor/app-insights/component?view=azure-cli-latest
az extension add -n application-insights
$instrumentationKey = ( az monitor app-insights component create --app $insights --location $location -g $rg --tags $tags | ConvertFrom-Json ).instrumentationKey
$instrumentationKey

# FUNCTION APP
az functionapp create -n $functionApp -g $rg --tags $tags --consumption-plan-location $location -s $webjobsStorage --app-insights $insights --app-insights-key $instrumentationKey

# APP SETTINGS
az functionapp config appsettings set -n $functionApp -g $rg --settings "APPINSIGHTS_INSTRUMENTATIONKEY=$instrumentationKey" "AzureWebJobsStorage=$webjobsStorageConnection" "EventHubConnectionString=$eventhubConnectionString" "ServiceBusConnectionString=$servicebusConnectionString"


# Tear down
# az group delete -n $rg --yes
