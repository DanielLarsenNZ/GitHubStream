$rg = 'githubstream-rg'
$location = 'australiaeast'
$eventhubNamespace = 'githubstream-hub'
$eventhub = 'githubstream'
$eventhubAuthRule = 'SenderListener1'
$servicebusNamespace = 'githubstream-bus'
$servicebusAuthRule = 'SenderReceiver1'
$job = 'githubstream-job'
$input = $eventhubNamespace

$ErrorActionPreference = 'Stop'

. ./Set-ContentVariables.ps1

Stop-AzStreamAnalyticsJob -ResourceGroupName $rg -Name $job

# Get Event Hub Event Key
$eventhubAuthKey = ( az eventhubs namespace authorization-rule keys list --resource-group $rg `
                      --namespace-name $eventhubnamespace --name $eventhubauthrule | ConvertFrom-Json ).primaryKey

# Service Bus Key
$sharedAccessPolicyKey = ( az servicebus namespace authorization-rule keys list -g $rg --namespace-name $servicebusNamespace --name $servicebusAuthRule | ConvertFrom-Json ).primaryKey

# Query
$query = Get-Content ./streamanalytics/githubstream-query.sql

# Job
$jobFile = './streamanalytics/_githubstream-job.json'
Copy-Item -Path './streamanalytics/githubstream-job.json' -Destination $jobFile -Force
Set-ContentVariables -Path $jobFile -Variables `
                      @{
                        location = $location;
                        eventhubNamespace = $eventhubNamespace;
                        eventhubAuthRule = $eventhubAuthRule;
                        eventhubAuthKey = $eventhubAuthKey;
                        eventhub = $eventhub;
                        serviceBusNamespace = $serviceBusNamespace;
                        sharedAccessPolicyName = $servicebusAuthRule;
                        sharedAccessPolicyKey = $sharedAccessPolicyKey;
                        query = $query -replace '\t', '  ' -join '\r\n'
                      }

New-AzStreamAnalyticsJob -ResourceGroupName $rg -File $jobFile -Name $job -Force

Start-AzStreamAnalyticsJob -ResourceGroupName $rg -Name $job -OutputStartMode 'JobStartTime'
