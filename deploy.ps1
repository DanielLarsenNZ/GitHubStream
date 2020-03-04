$location = 'australiaeast'
$loc = 'aue'
$rg = 'githubstream-rg'
$tags = 'project=GitHubStream', 'owner=dalars'
$webjobsStorage = "githubstream$loc"
$functionApp = "githubstream-$loc-fn"
$eventhubNamespace = 'githubstream-hub'
$eventhubs = 'githubstream'
$eventhubAuthRule = 'SenderListener1'

# Consider these settings for scale
$eventhubsSku = 'Basic'
$eventhubsRetentionDays = 1
$eventhubsPartitions = 12    # 2 - 32. Cannot be changed after deployment. Good discussion here: https://medium.com/@iizotov/azure-functions-and-event-hubs-optimising-for-throughput-549c7acd2b75

$ErrorActionPreference = 'Stop'

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