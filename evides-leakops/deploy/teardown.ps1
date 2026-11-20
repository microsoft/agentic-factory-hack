<#
.SYNOPSIS
  Deletes the whole Evides LeakOps demo: the resource group with everything in it (Foundry, Cosmos DB, storage, apps, monitoring).

.DESCRIPTION
  The resource group only holds this demo, so deleting it is the complete, cheapest clean-up. Foundry accounts are soft-deleted by
  Azure; this script purges the Foundry resource afterwards so the name and the model quota are free again.
  Use -WhatIf to see what would be deleted.

.EXAMPLE
  ./evides-leakops/deploy/teardown.ps1 -WhatIf
  ./evides-leakops/deploy/teardown.ps1
#>
[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param(
    [string] $SubscriptionId = 'a39be7cd-9ae9-42cf-b42f-dc6e45a82464',
    [string] $ResourceGroup = 'rg-evides-ai-demo'
)

$ErrorActionPreference = 'Stop'

$azExe = (Get-Command az -CommandType Application | Select-Object -First 1).Source   # not "az": command names are case insensitive and that would call the function below
function Az {
    $output = & $azExe @args 2>&1
    if ($LASTEXITCODE -ne 0) { throw "az $($args -join ' ') failed:`n$($output | Out-String)" }
    $output
}

. (Join-Path $PSScriptRoot 'tenant-guard.ps1'); Assert-LeakOpsTenant $SubscriptionId

$exists = (& $azExe group exists --subscription $SubscriptionId -n $ResourceGroup 2>$null | Out-String).Trim()
if ($exists -ne 'true') { Write-Host "Resource group $ResourceGroup does not exist. Nothing to delete."; return }

# Only a group that was made by deploy.ps1 may be deleted by this script.
$tag = (& $azExe group show --subscription $SubscriptionId -n $ResourceGroup --query "tags.project" -o tsv 2>$null | Out-String).Trim()
if ($tag -ne 'evides-leakops') { throw "Resource group $ResourceGroup is not tagged project=evides-leakops. Refusing to delete it." }

$resources = Az resource list --subscription $SubscriptionId -g $ResourceGroup --query "[].{type:type, name:name}" -o json | Out-String | ConvertFrom-Json
Write-Host "Resources in ${ResourceGroup}:"
$resources | ForEach-Object { Write-Host "   $($_.type)  $($_.name)" }

$foundry = @($resources | Where-Object { $_.type -eq 'Microsoft.CognitiveServices/accounts' })
$location = (Az group show --subscription $SubscriptionId -n $ResourceGroup --query location -o tsv | Out-String).Trim()

if ($PSCmdlet.ShouldProcess("resource group $ResourceGroup", 'Delete')) {
    Az group delete --subscription $SubscriptionId -n $ResourceGroup --yes -o none | Out-Null
    foreach ($account in $foundry) {
        Az cognitiveservices account purge --subscription $SubscriptionId -g $ResourceGroup -n $account.name -l $location -o none | Out-Null
        Write-Host "   purged Foundry resource $($account.name)"
    }
    Write-Host "Deleted $ResourceGroup."
}
