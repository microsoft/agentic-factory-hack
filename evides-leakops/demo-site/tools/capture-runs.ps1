<#
.SYNOPSIS
  Records the burst and the false-alarm scenario on the deployed workflow and rebuilds demo-site/js/captured.js from them.

.DESCRIPTION
  Each run costs about 0.03 EUR of model use. The operator key is read from the app settings for this run only and never written to a file.
  The runs are real: they book an incident, reserve stock and publish on the fake Waterstoring site, so clear the runtime data afterwards if you
  want a clean demo (see "Before the demo" in DEMO.md). Needs the Azure CLI, curl and Node.js.

.EXAMPLE
  $env:AZURE_CONFIG_DIR = "$HOME\.azure-macaw"
  ./evides-leakops/demo-site/tools/capture-runs.ps1
#>
#requires -Version 7.0
[CmdletBinding()]
param(
    [string] $SubscriptionId = 'a39be7cd-9ae9-42cf-b42f-dc6e45a82464',
    [string] $ResourceGroup = 'rg-evides-ai-demo',
    [string] $WorkflowApp = 'leakops-workflow-88546e'
)
$ErrorActionPreference = 'Stop'
$azExe = (Get-Command az -CommandType Application | Select-Object -First 1).Source
$key = (& $azExe webapp config appsettings list --subscription $SubscriptionId -g $ResourceGroup -n $WorkflowApp --query "[?name=='OPERATOR_KEY'].value | [0]" -o tsv | Out-String).Trim()
if (-not $key) { throw 'Could not read the operator key.' }
$base = "https://$WorkflowApp.azurewebsites.net"
$work = Join-Path ([IO.Path]::GetTempPath()) "leakops-capture-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $work | Out-Null
try {
    $scenarios = Join-Path $work 'scenarios.json'
    Invoke-RestMethod "$base/api/scenarios" -Headers @{ 'X-Operator-Key' = $key } | ConvertTo-Json -Depth 8 | Set-Content $scenarios -Encoding utf8
    $files = @{}
    foreach ($id in 'scn-burst-cast-iron', 'scn-false-alarm-planned-works') {
        $body = Join-Path $work "$id.json"; [IO.File]::WriteAllText($body, (@{ scenarioId = $id } | ConvertTo-Json -Compress))
        $files[$id] = Join-Path $work "$id.sse"
        Write-Host "   running $id ..."
        & curl.exe -sS -N --max-time 300 -X POST "$base/api/runs" -H "X-Operator-Key: $key" -H 'Content-Type: application/json' --data-binary "@$body" -o $files[$id]
        if ($LASTEXITCODE -ne 0) { throw "The run of $id failed." }
    }
    $out = Join-Path (Split-Path -Parent $PSScriptRoot) 'js/captured.js'
    & node (Join-Path $PSScriptRoot 'build-captured.js') $scenarios $files['scn-burst-cast-iron'] $files['scn-false-alarm-planned-works'] $out
    if ($LASTEXITCODE -ne 0) { throw 'build-captured.js failed.' }
    Write-Host "   ok   $out"
}
finally { Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue }
