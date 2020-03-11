$rg = 'githubstream-rg'
$location = 'australiaeast'
$eventhubNamespace = 'githubstream-hub'
$eventhubs = 'githubstream'
$eventhubAuthRule = 'SenderListener1'
$saJob = 'githubstream-job'

function RewriteVariable
{
  param (
    [string] $FilePath,
    [string] $Placeholder,
    [string] $ReplacementValue
  )
  (Get-Content $FilePath).replace($Placeholder, $ReplacementValue) | Set-Content $FilePath
}

# Job
$jobFile = './streamanalytics/_githubstream-job.json'
Copy-Item -Path './streamanalytics/githubstream-job.json' `
          -Destination $jobFile -Force
RewriteVariable -FilePath $jobFile `
                -Placeholder '$location' -ReplacementValue $location

New-AzStreamAnalyticsJob -ResourceGroupName $rg -File $jobFile `
  -Name $saJob -Force

# Input
