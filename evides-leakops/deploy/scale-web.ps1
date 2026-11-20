<#
.SYNOPSIS
  Switches the web plan of Evides LeakOps between Basic B1 (about 11.80 EUR a month, always on) and Free F1 (0 EUR).

.DESCRIPTION
  Use F1 when you are not demoing and B1 shortly before you are. Nothing is lost: the public incidents live in Cosmos DB, and
  the function apps cost nothing when idle. On F1 the apps sleep after a while and the first request is slow; F1 also has a daily
  CPU quota of 60 minutes, so use B1 for a real demo.

.EXAMPLE
  ./evides-leakops/deploy/scale-web.ps1 -Sku F1     # idle: free
  ./evides-leakops/deploy/scale-web.ps1 -Sku B1     # before a demo
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [ValidateSet('B1', 'F1')] [string] $Sku,
    [string] $SubscriptionId = 'a39be7cd-9ae9-42cf-b42f-dc6e45a82464',
    [string] $ResourceGroup = 'rg-evides-ai-demo'
)

$ErrorActionPreference = 'Stop'
# The same suffix deploy.ps1 derived from the subscription and resource group.
$Suffix = (([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes("$SubscriptionId/$ResourceGroup")) | ForEach-Object { $_.ToString('x2') }) -join '').Substring(0, 6)
$plan = "leakops-web-plan-$Suffix"
$apps = "leakops-waterstoring-$Suffix", "leakops-workflow-$Suffix"

$azExe = (Get-Command az -CommandType Application | Select-Object -First 1).Source   # not "az": command names are case insensitive and that would call the function below
function Az {
    $output = & $azExe @args 2>&1
    if ($LASTEXITCODE -ne 0) { throw "az $($args -join ' ') failed:`n$($output | Out-String)" }
    $output
}

. (Join-Path $PSScriptRoot 'tenant-guard.ps1'); Assert-LeakOpsTenant $SubscriptionId

if ($Sku -eq 'F1') {
    # The free plan has no "always on", so it is switched off before the plan is changed.
    foreach ($app in $apps) { Az webapp config set --subscription $SubscriptionId -g $ResourceGroup -n $app --always-on false -o none | Out-Null }
    Az appservice plan update --subscription $SubscriptionId -g $ResourceGroup -n $plan --sku F1 -o none | Out-Null
}
else {
    Az appservice plan update --subscription $SubscriptionId -g $ResourceGroup -n $plan --sku B1 -o none | Out-Null
    foreach ($app in $apps) { Az webapp config set --subscription $SubscriptionId -g $ResourceGroup -n $app --always-on true -o none | Out-Null }
}

$now = Az appservice plan show --subscription $SubscriptionId -g $ResourceGroup -n $plan --query sku.name -o tsv
Write-Host "Web plan $plan is now $($now.Trim())."
