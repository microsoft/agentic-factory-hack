<#
.SYNOPSIS
  One-off: generates AI portraits (fictional people) for the 8 technicians and saves them in the Waterstoring site.

.DESCRIPTION
  Not part of the normal deploy. The result (8 small JPG files) is committed, so deploys need no image model and cost nothing.
  The script creates a temporary image-model deployment on the Foundry resource, generates one portrait per technician with Entra
  authentication (no keys), resizes them, and deletes the deployment again.
  The faces are generated and do not depict real people. The technicians page labels them "AI-generated, fictional person".

.EXAMPLE
  $env:AZURE_CONFIG_DIR = "$HOME\.azure-macaw"
  ./deploy/generate-technician-photos.ps1 -Only T-02      # one portrait to look at first
  ./deploy/generate-technician-photos.ps1                 # all eight
#>
[CmdletBinding()]
param(
    [string]$Subscription = 'a39be7cd-9ae9-42cf-b42f-dc6e45a82464',
    [string]$ResourceGroup = 'rg-evides-ai-demo',
    [string]$Account = 'evides-foundry-88546e',
    [string]$Model = 'gpt-image-1.5',
    [string]$ModelVersion = '2025-12-16',
    [string]$DeploymentName = 'tmp-portraits',
    [string[]]$Only = @(),
    [switch]$KeepDeployment
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$outDir = Join-Path $root 'src/LeakOps.Waterstoring/wwwroot/technicians'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

# Portrait descriptions per technician: the look is invented, only the first name hints at gender.
$people = [ordered]@{
    'T-01' = 'a man in his mid 40s, short grey-brown hair, light stubble, friendly calm expression'
    'T-02' = 'a woman in her mid 30s of North African descent, dark hair tied back, confident warm smile'
    'T-03' = 'a man in his late 30s, broad build, short blond hair, cheerful grin'
    'T-04' = 'a woman in her early 40s, shoulder-length auburn hair, focused friendly look'
    'T-05' = 'a man in his late 20s, short dark brown hair, clean shaven, relaxed smile'
    'T-06' = 'a woman in her late 20s, blond ponytail, freckles, bright smile'
    'T-07' = 'a man in his mid 50s, grey beard, kind tired eyes, slight smile'
    'T-08' = 'a woman in her mid 30s of Turkish descent, dark wavy hair, safety glasses pushed up on her head, warm smile'
}

. (Join-Path $PSScriptRoot 'tenant-guard.ps1'); Assert-LeakOpsTenant $Subscription

$endpoint = az cognitiveservices account show --subscription $Subscription -g $ResourceGroup -n $Account --query properties.endpoint -o tsv
if (-not $endpoint) { throw 'Could not read the Foundry endpoint. Is AZURE_CONFIG_DIR / az login right?' }
$endpoint = $endpoint.TrimEnd('/')

$existing = az cognitiveservices account deployment list --subscription $Subscription -g $ResourceGroup -n $Account --query "[?name=='$DeploymentName'].name" -o tsv
if (-not $existing) {
    Write-Host "Creating temporary deployment '$DeploymentName' ($Model)..."
    az cognitiveservices account deployment create --subscription $Subscription -g $ResourceGroup -n $Account `
        --deployment-name $DeploymentName --model-name $Model --model-version $ModelVersion --model-format OpenAI `
        --sku-name GlobalStandard --sku-capacity 1 -o none
    if ($LASTEXITCODE -ne 0) { throw 'Deployment of the image model failed.' }
}

Add-Type -AssemblyName System.Drawing
try {
    $token = az account get-access-token --resource https://cognitiveservices.azure.com --query accessToken -o tsv
    $headers = @{ Authorization = "Bearer $token" }
    $uri = "$endpoint/openai/deployments/$DeploymentName/images/generations?api-version=2025-04-01-preview"

    foreach ($id in $people.Keys) {
        if ($Only.Count -gt 0 -and $Only -notcontains $id) { continue }
        $prompt = "Realistic head-and-shoulders portrait photo of a fictional water utility field technician: $($people[$id]). " +
                  "Wearing a hi-vis safety jacket with teal accents and a dark work shirt. Soft neutral grey-blue studio background, " +
                  "natural lighting, looking at the camera, professional ID-badge style photo. No text, no logos, no watermark."
        $body = @{ prompt = $prompt; size = '1024x1024'; n = 1; quality = 'medium'; output_format = 'jpeg' } | ConvertTo-Json
        Write-Host "Generating $id..."

        $response = $null
        foreach ($attempt in 1..4) {
            try {
                $response = Invoke-RestMethod -Method Post -Uri $uri -Headers $headers -ContentType 'application/json' -Body $body -TimeoutSec 180
                break
            }
            catch {
                if ($attempt -eq 4) { throw }
                Write-Warning "Attempt $attempt failed ($($_.Exception.Message)); retrying..."
                Start-Sleep -Seconds (10 * $attempt)
            }
        }

        # Resize to 384x384 JPEG: small enough to commit and plenty for a card.
        $bytes = [Convert]::FromBase64String($response.data[0].b64_json)
        $source = [System.Drawing.Image]::FromStream([IO.MemoryStream]::new($bytes))
        $target = [System.Drawing.Bitmap]::new(384, 384)
        $g = [System.Drawing.Graphics]::FromImage($target)
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.DrawImage($source, 0, 0, 384, 384)
        $g.Dispose()
        $jpeg = [System.Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() | Where-Object MimeType -eq 'image/jpeg'
        $encoder = [System.Drawing.Imaging.EncoderParameters]::new(1)
        $encoder.Param[0] = [System.Drawing.Imaging.EncoderParameter]::new([System.Drawing.Imaging.Encoder]::Quality, [long]82)
        $file = Join-Path $outDir "$id.jpg"
        $target.Save($file, $jpeg, $encoder)
        $target.Dispose(); $source.Dispose()
        Write-Host ("  saved {0} ({1:N0} KB)" -f $file, ((Get-Item $file).Length / 1KB))
    }
}
finally {
    if (-not $KeepDeployment) {
        Write-Host "Deleting temporary deployment '$DeploymentName'..."
        az cognitiveservices account deployment delete --subscription $Subscription -g $ResourceGroup -n $Account --deployment-name $DeploymentName -o none
    }
}
Write-Host 'Done. Look at every image before committing.'
