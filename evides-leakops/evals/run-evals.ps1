<#
.SYNOPSIS
  Runs the LeakOps evaluations in the Foundry project, so the results show up in the portal (project > Evaluations).

.DESCRIPTION
  Foundry sends each test question in evals/leakops-evals.json to the registered agent, then scores the answers:
    - verdict_correct / segment_correct / breach_correct: does the answer hold the expected value (checked against the simulation's
      ground truth, which only these tests know)?
    - task_adherence: an AI judge (the gpt-5.4 deployment) checks whether the agent followed its task and rules.
    - tool_call_success: did the agent's MCP tool calls work?
  Two evaluations are created: TriageAgent on the two scenarios plus two guardrail cases, and DiagnosticAgent on the burst scenario.
  The agents run against the latest registered version. A run takes a few minutes and costs a few cents of model use.

.EXAMPLE
  $env:AZURE_CONFIG_DIR = "$HOME\.azure-macaw"     # only when your Azure CLI default login is another tenant
  ./evides-leakops/evals/run-evals.ps1
  ./evides-leakops/evals/run-evals.ps1 -Only triage
#>
#requires -Version 7.0
[CmdletBinding()]
param(
    [string] $SubscriptionId = 'a39be7cd-9ae9-42cf-b42f-dc6e45a82464',
    [string] $ProjectEndpoint = 'https://evides-foundry-88546e.services.ai.azure.com/api/projects/evides-leakops',
    [string] $JudgeModel = 'gpt-5.4',
    [ValidateSet('triage', 'diagnostic', 'knowledge', 'all')] [string] $Only = 'all'
)

$ErrorActionPreference = 'Stop'
$azExe = (Get-Command az -CommandType Application | Select-Object -First 1).Source
$token = (& $azExe account get-access-token --resource https://ai.azure.com --subscription $SubscriptionId --query accessToken -o tsv | Out-String).Trim()
if (-not $token) { throw 'Could not get an access token. Run az login first.' }
$headers = @{ Authorization = "Bearer $token" }
$data = Get-Content (Join-Path $PSScriptRoot 'leakops-evals.json') -Raw | ConvertFrom-Json

function Invoke-Foundry([string] $method, [string] $path, $body = $null) {
    $args = @{ Uri = "$ProjectEndpoint/$path"; Method = $method; Headers = $headers; ContentType = 'application/json' }
    if ($null -ne $body) { $args.Body = ($body | ConvertTo-Json -Depth 20) }
    Invoke-RestMethod @args
}

function Get-LatestVersion([string] $agent) {
    $versions = (Invoke-Foundry Get "agents/$agent/versions?api-version=v1").data
    ($versions | Sort-Object { [int] $_.version } -Descending | Select-Object -First 1).version
}

function New-Check([string] $name, [string] $pattern) {
    @{ type = 'string_check'; name = $name; input = '{{sample.output_text}}'; operation = 'like'; reference = "{{item.$pattern}}" }
}

function New-Judge([string] $evaluator) {
    @{
        type = 'azure_ai_evaluator'; name = $evaluator; evaluator_name = "builtin.$evaluator"
        initialization_parameters = @{ deployment_name = $JudgeModel }
        data_mapping = @{ query = '{{item.query}}'; response = '{{sample.output_items}}' }
    }
}

function Start-Evaluation([string] $title, $suite, [string[]] $fields, $criteria) {
    $agent = $suite.agent
    $version = Get-LatestVersion $agent
    $properties = @{ query = @{ type = 'string' } }
    foreach ($field in $fields) { $properties[$field] = @{ type = 'string' } }

    $eval = Invoke-Foundry Post 'openai/v1/evals' @{
        name = $title
        data_source_config = @{ type = 'custom'; item_schema = @{ type = 'object'; properties = $properties; required = @('query') }; include_sample_schema = $true }
        testing_criteria = $criteria
    }

    $items = foreach ($item in $suite.items) {
        $content = @{ query = $item.query }
        foreach ($field in $fields) { $content[$field] = $item.$field }
        @{ item = $content }
    }
    $run = Invoke-Foundry Post "openai/v1/evals/$($eval.id)/runs" @{
        name = "$agent v$version"
        data_source = @{
            type = 'azure_ai_target_completions'
            source = @{ type = 'file_content'; content = @($items) }
            input_messages = @{ type = 'template'; template = @(@{ type = 'message'; role = 'user'; content = @{ type = 'input_text'; text = '{{item.query}}' } }) }
            target = @{ type = 'azure_ai_agent'; name = $agent; version = "$version" }
        }
    }
    Write-Host "`n== $title  ($agent v$version, $(@($suite.items).Count) question(s))" -ForegroundColor Cyan

    while ($run.status -in 'queued', 'in_progress') {
        Start-Sleep -Seconds 15
        $run = Invoke-Foundry Get "openai/v1/evals/$($eval.id)/runs/$($run.id)"
    }
    Write-Host "   status $($run.status): $($run.result_counts.passed) of $($run.result_counts.total) questions passed every check"
    foreach ($result in $run.per_testing_criteria_results) {
        Write-Host ("   {0,-20} passed {1}  failed {2}  errored {3}  skipped {4}" -f $result.testing_criteria, $result.passed, $result.failed, $result.errored, $result.skipped)
    }
    Write-Host "   portal: $($run.report_url)"
}

if ($Only -in 'triage', 'all') {
    Start-Evaluation 'leakops-triage-scenarios' $data.triage @('expected_verdict') @(
        (New-Check 'verdict_correct' 'expected_verdict'), (New-Judge 'task_adherence'), (New-Judge 'tool_call_success'))
}
if ($Only -in 'diagnostic', 'all') {
    Start-Evaluation 'leakops-diagnostic-burst' $data.diagnostic @('expected_segment', 'expected_breach') @(
        (New-Check 'segment_correct' 'expected_segment'), (New-Check 'breach_correct' 'expected_breach'), (New-Judge 'task_adherence'))
}
# The knowledge layer (Foundry IQ): the agents must use the documents, and the tool calls to the knowledge base must work.
# The guardrails are tested by the unit tests and by the cases above, which must keep passing with knowledge switched on.
if ($Only -in 'knowledge', 'all') {
    Start-Evaluation 'leakops-diagnostic-knowledge' $data.diagnostic_knowledge @('expected_segment', 'expected_breach', 'expected_backup', 'expected_care') @(
        (New-Check 'segment_correct' 'expected_segment'), (New-Check 'breach_correct' 'expected_breach'),
        (New-Check 'backup_valve_named' 'expected_backup'), (New-Check 'care_home_first' 'expected_care'),
        (New-Judge 'task_adherence'), (New-Judge 'tool_call_success'))
    Start-Evaluation 'leakops-communications-knowledge' $data.communications_knowledge @('expected_hedge', 'expected_advice') @(
        (New-Check 'cause_only_suspected' 'expected_hedge'), (New-Check 'advice_left_to_the_system' 'expected_advice'),
        (New-Judge 'task_adherence'), (New-Judge 'tool_call_success'))
    Start-Evaluation 'leakops-procurement-knowledge' $data.procurement_knowledge @('expected_package', 'expected_policy') @(
        (New-Check 'section_and_couplings' 'expected_package'), (New-Check 'policy_cited' 'expected_policy'),
        (New-Judge 'task_adherence'), (New-Judge 'tool_call_success'))
}
