# Shared guard for the scripts that change Azure: the LeakOps demo is only ever deployed to the Macaw tenant
# (subscription a39be7cd-9ae9-42cf-b42f-dc6e45a82464, resource group rg-evides-ai-demo, the defaults of every script).
# Dot-source it and call Assert-LeakOpsTenant before the first change, so a login in another tenant stops the script at once.

function Assert-LeakOpsTenant([string] $SubscriptionId) {
    $expected = '05baed4d-e049-45c7-b47d-c99c66fdb589'
    $azPath = (Get-Command az -CommandType Application | Select-Object -First 1).Source
    $tenant = (& $azPath account show --subscription $SubscriptionId --query tenantId -o tsv 2>$null | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $tenant -ne $expected) {
        $found = if ($tenant) { "it is in tenant $tenant" } else { 'the current Azure CLI login cannot see it' }
        throw ("Subscription $SubscriptionId is not in the Macaw tenant $expected ($found). The LeakOps demo is only deployed there.`n" +
               "Sign in with a separate Azure CLI profile and run the script again:`n" +
               "  `$env:AZURE_CONFIG_DIR = `"`$HOME\.azure-macaw`"; az login --tenant $expected")
    }
}
