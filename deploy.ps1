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
$topics = @{Topic = 'comments'; Subscription = 'CommentLogger' }, @{Topic = 'pushes'; Subscription = 'PushCounter' }, `
    @{Topic = 'pullrequests'; Subscription = 'PRProcessor' }, @{Topic = 'releases'; Subscription = 'ReleaseProcessor' }, `
    @{Topic = 'stars'; Subscription = 'StarCounter' }, @{Topic = 'watches'; Subscription = 'WatchCounter' }
$servicebusAuthRule = 'SenderReceiver1'
$servicebusSku = 'Standard'
$eventhubsSku = 'Basic'
$eventhubsRetentionDays = 1
$eventhubsPartitions = 2    # 2 - 32. Cannot be changed after deployment. Good discussion here: https://medium.com/@iizotov/azure-functions-and-event-hubs-optimising-for-throughput-549c7acd2b75
$cosmos = 'githubstream-cosmos'
$cosmosdb = 'githubstream-db'
$collection = 'dashboard'

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
$eventhubConnectionString = ( az eventhubs namespace authorization-rule keys list --resource-group $rg --namespace-name $eventhubnamespace --name $eventhubauthrule | ConvertFrom-Json ).primaryConnectionString


# SERVICE BUS
# https://docs.microsoft.com/en-us/cli/azure/servicebus/namespace?view=azure-cli-latest#az-servicebus-namespace-create

# Create namespace, queue and auth rule
az servicebus namespace create -g $rg --name $servicebusNamespace --location $location --tags $tags --sku $servicebusSku

foreach ($topic in $topics) {
    az servicebus topic create -g $rg --namespace-name $servicebusNamespace --name $topic.Topic #--default-message-time-to-live 'P14D'
    az servicebus topic subscription create -g $rg --namespace-name $servicebusNamespace --topic-name $topic.Topic -n $topic.Subscription
}

az servicebus namespace authorization-rule create -g $rg --namespace-name $servicebusNamespace --name $servicebusAuthRule --rights Listen Send
$servicebusConnectionString = ( az servicebus namespace authorization-rule keys list -g $rg --namespace-name $servicebusNamespace --name $servicebusAuthRule | ConvertFrom-Json ).primaryConnectionString


# STORAGE ACCOUNT
# https://docs.microsoft.com/en-us/cli/azure/storage/account?view=azure-cli-latest#az-storage-account-create
az storage account create -n $webjobsStorage -g $rg -l $location --tags $tags --sku Standard_LRS
$webjobsStorageConnection = ( az storage account show-connection-string -g $rg -n $webjobsStorage | ConvertFrom-Json ).connectionString


# APPLICATION INSIGHTS
#  https://docs.microsoft.com/en-us/cli/azure/ext/application-insights/monitor/app-insights/component?view=azure-cli-latest
az extension add -n application-insights
$instrumentationKey = ( az monitor app-insights component create --app $insights --location $location -g $rg --tags $tags | ConvertFrom-Json ).instrumentationKey


# COSMOS DB
az cosmosdb create -n $cosmos -g $rg 
az cosmosdb database create -n $cosmos -g $rg --db-name $cosmosdb
az cosmosdb collection create -g $rg --collection-name $collection --db-name $cosmosdb -n $cosmos `
    --partition-key-path '/timestamp' --throughput 400

$cosmosConnection = ( az cosmosdb keys list -n $cosmos -g $rg --type 'connection-strings' | ConvertFrom-Json ).connectionStrings[0].connectionString
$env:COSMOSPRIMARYMASTERKEY = ( az cosmosdb keys list -n $cosmos -g $rg --type 'keys' | ConvertFrom-Json ).primaryMasterKey

# FUNCTION APP
az functionapp create -n $functionApp -g $rg --tags $tags --consumption-plan-location $location -s $webjobsStorage --app-insights $insights --app-insights-key $instrumentationKey


# APP SETTINGS
# Get Client Id and Secret from local.settings.json
$config = Get-Content -Path './GithubStream/local.settings.json' | ConvertFrom-Json 
az functionapp config appsettings set -n $functionApp -g $rg --settings `
    "APPINSIGHTS_INSTRUMENTATIONKEY=$instrumentationKey" `
    "AzureWebJobsStorage=$webjobsStorageConnection" `
    "EventHubConnectionString=$eventhubConnectionString" `
    "ServiceBusConnectionString=$servicebusConnectionString" `
    "GitHubAppClientId=$($config.Values.GitHubAppClientId)" `
    "GitHubAppClientSecret=$($config.Values.GitHubAppClientSecret)" `
    "CosmosConnectionString=$cosmosConnection" `
    "CosmosDbName=$cosmosdb" `
    "CosmosCollection=$collection"


# Tear down
# az group delete -n $rg --yes
