<#
.SYNOPSIS
  ONE-OFF: removes the Evides LeakOps resources of the first deployment from the tire-factory resource group.

.DESCRIPTION
  The first deployment of LeakOps reused the tire-factory resource group and shared resources. The demo now has its own resource
  group (deploy.ps1), so this script cleans up the old copy: everything tagged project=evides-leakops in rg-tire-factory-demo and the
  access rights that belong to them. It does NOT touch the tire-factory resources (Cosmos DB, storage account, Application Insights,
  Foundry), nor the simulated data in the LeakOpsDB database. With -RemoveIncidents it also deletes the Cosmos container with the
  public incidents and, with -RemoveLeakOpsData, the LeakOpsDB database itself.
  Use -WhatIf to see what would be removed.

.EXAMPLE
  ./evides-leakops/deploy/remove-from-tire-factory.ps1 -WhatIf
  ./evides-leakops/deploy/remove-from-tire-factory.ps1 -RemoveLeakOpsData
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string] $SubscriptionId = 'a39be7cd-9ae9-42cf-b42f-dc6e45a82464',
    [string] $ResourceGroup = 'rg-tire-factory-demo',
    [string] $Suffix = 'lfp4hhftznza4',
    [switch] $RemoveIncidents,
    [switch] $RemoveLeakOpsData
)

$ErrorActionPreference = 'Stop'

$azExe = (Get-Command az -CommandType Application | Select-Object -First 1).Source   # not "az": command names are case insensitive and that would call the function below
function Az {
    $output = & $azExe @args 2>&1
    if ($LASTEXITCODE -ne 0) { throw "az $($args -join ' ') failed:`n$($output | Out-String)" }
    $output
}

$resources = Az resource list --subscription $SubscriptionId --tag project=evides-leakops --query "[].{id:id, name:name, type:type, rg:resourceGroup}" -o json | Out-String | ConvertFrom-Json
$resources = @($resources | Where-Object { $_.rg -ieq $ResourceGroup })   # "az resource list" cannot combine --tag with --resource-group
if (-not $resources) { Write-Host 'Nothing tagged project=evides-leakops was found. Nothing to remove.'; return }

Write-Host 'Resources that will be removed:'
$resources | ForEach-Object { Write-Host "   $($_.type)  $($_.name)" }

# 1. Access rights that point at the identities of the apps (they would be left behind as "unknown" otherwise).
$principals = foreach ($app in $resources | Where-Object { $_.type -eq 'Microsoft.Web/sites' }) {
    (& $azExe resource show --ids $app.id --query identity.principalId -o tsv 2>$null)
}
foreach ($principal in $principals | Where-Object { $_ }) {
    $principal = $principal.Trim()
    $cosmosRoles = & $azExe cosmosdb sql role assignment list --subscription $SubscriptionId -g $ResourceGroup -a "msagthack-cosmos-$Suffix" --query "[?principalId=='$principal'].name" -o tsv 2>$null
    foreach ($role in $cosmosRoles | Where-Object { $_ }) {
        if ($PSCmdlet.ShouldProcess("Cosmos role assignment $role", 'Remove')) {
            Az cosmosdb sql role assignment delete --subscription $SubscriptionId -g $ResourceGroup -a "msagthack-cosmos-$Suffix" --role-assignment-id $role.Trim() --yes -o none | Out-Null
        }
    }
    $assignments = & $azExe role assignment list --subscription $SubscriptionId --all --assignee $principal --query "[].id" -o tsv 2>$null
    foreach ($assignment in $assignments | Where-Object { $_ }) {
        if ($PSCmdlet.ShouldProcess("Azure role assignment $assignment", 'Remove')) { Az role assignment delete --ids $assignment.Trim() -o none | Out-Null }
    }
}

# 2. The apps and plans (apps first, then the plans they ran on).
foreach ($resource in ($resources | Sort-Object { if ($_.type -eq 'Microsoft.Web/sites') { 0 } else { 1 } })) {
    if ($PSCmdlet.ShouldProcess("$($resource.type) $($resource.name)", 'Delete')) { Az resource delete --ids $resource.id -o none | Out-Null }
}

# 3. The deployment packages the function apps kept in the shared storage account.
foreach ($container in "leakops-mcp-$Suffix-deploy", "leakops-agents-$Suffix-deploy") {
    if ($PSCmdlet.ShouldProcess("blob container $container", 'Delete')) {
        & $azExe storage container delete --subscription $SubscriptionId --account-name "msagthacksa$Suffix" -n $container --auth-mode login -o none 2>$null
    }
}

if ($RemoveLeakOpsData -and $PSCmdlet.ShouldProcess('Cosmos database LeakOpsDB', 'Delete')) {
    Az cosmosdb sql database delete --subscription $SubscriptionId -g $ResourceGroup -a "msagthack-cosmos-$Suffix" -n LeakOpsDB --yes -o none | Out-Null
}
elseif ($RemoveIncidents -and $PSCmdlet.ShouldProcess('Cosmos container WaterstoringIncidents', 'Delete')) {
    Az cosmosdb sql container delete --subscription $SubscriptionId -g $ResourceGroup -a "msagthack-cosmos-$Suffix" -d LeakOpsDB -n WaterstoringIncidents --yes -o none | Out-Null
}

Write-Host 'Done. The tire-factory resources are untouched.'
