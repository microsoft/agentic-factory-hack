#!/usr/bin/env pwsh
# Copyright (c) Microsoft Corporation.
# SPDX-License-Identifier: MIT
#Requires -Version 7.0

<#
.SYNOPSIS
    Create a Microsoft Entra security group, one coach, and team participants.
.DESCRIPTION
    Use Azure CLI to create a security group named TeamName, a TeamName-coach
    account, and TeamName-participant01, TeamName-participant02, and so on.
    Optionally add the team group as a member of an existing parent group.
    Reuse existing matching
    groups and users without resetting passwords or removing members. Export
    user names and new users' independently generated random temporary passwords
    to a plaintext CSV file.
.PARAMETER TeamName
    Team name, containing up to 40 letters, digits, or hyphens, starting with
    a letter or digit. Also used as the group name and user name prefix.
.PARAMETER ParticipantCount
    Number of participants, excluding the additional coach. UserCount is an alias.
.PARAMETER TenantDomain
    Verified, managed Entra domain, such as contoso.onmicrosoft.com.
.PARAMETER OutputCsvPath
    Credentials CSV path. Defaults to a timestamped file beside this script.
    Existing files are never overwritten. The parent directory must exist.
.PARAMETER ParentGroupId
    Optional object ID of an existing, assigned-membership security group.
    The team group is added as a member of this parent group.
.EXAMPLE
    .\create-users.ps1 -TeamName team-blue -ParticipantCount 5 -TenantDomain contoso.onmicrosoft.com
.EXAMPLE
    .\create-users.ps1 -TeamName team-blue -ParticipantCount 5 -TenantDomain contoso.onmicrosoft.com -WhatIf
.NOTES
    Requires PowerShell 7+, Azure CLI, az login --tenant <tenant-id>, and
    directory permissions to create users, groups, and group memberships.
    Azure CLI receives the password as a process argument; use a trusted host.
#>

[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[a-zA-Z0-9][a-zA-Z0-9-]{0,39}$')]
    [string]$TeamName,

    [Parameter(Mandatory = $true)]
    [Alias('UserCount')]
    [ValidateRange(1, 999)]
    [int]$ParticipantCount,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^(?=.{1,253}$)[a-zA-Z0-9](?:[a-zA-Z0-9-]*[a-zA-Z0-9])?(?:\.[a-zA-Z0-9](?:[a-zA-Z0-9-]*[a-zA-Z0-9])?)+$')]
    [string]$TenantDomain,

    [Parameter(Mandatory = $false)]
    [ValidateNotNullOrEmpty()]
    [string]$OutputCsvPath = (Join-Path $PSScriptRoot "$TeamName-user-credentials-$(Get-Date -Format 'yyyyMMdd-HHmmss-fff').csv"),

    [Parameter(Mandatory = $false)]
    [ValidatePattern('^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')]
    [string]$ParentGroupId
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

#region Functions

function New-RandomPassword {
    <#
    .SYNOPSIS
        Generate a cryptographically random 20-character temporary password.
    .OUTPUTS
        System.String. Password containing uppercase, lowercase, digits, and symbols.
    #>
    [CmdletBinding()]
    [OutputType([string])]
    param()

    $CharacterSets = @(
        'ABCDEFGHIJKLMNOPQRSTUVWXYZ',
        'abcdefghijklmnopqrstuvwxyz',
        '0123456789',
        '!#_?'
    )
    $Alphabet = $CharacterSets -join ''
    $Characters = [System.Collections.Generic.List[char]]::new()
    foreach ($CharacterSet in $CharacterSets) {
        $Characters.Add($CharacterSet[[System.Security.Cryptography.RandomNumberGenerator]::GetInt32($CharacterSet.Length)])
    }
    while ($Characters.Count -lt 20) {
        $Characters.Add($Alphabet[[System.Security.Cryptography.RandomNumberGenerator]::GetInt32($Alphabet.Length)])
    }
    for ($Index = $Characters.Count - 1; $Index -gt 0; $Index--) {
        $SwapIndex = [System.Security.Cryptography.RandomNumberGenerator]::GetInt32($Index + 1)
        $Temporary = $Characters[$Index]
        $Characters[$Index] = $Characters[$SwapIndex]
        $Characters[$SwapIndex] = $Temporary
    }
    return $Characters -join ''
}

function Invoke-AzureCli {
    <#
    .SYNOPSIS
        Run Azure CLI, check its exit code, and parse JSON output.
    .PARAMETER Arguments
        Azure CLI arguments, without output-format or logging flags.
    .OUTPUTS
        System.Object. Parsed JSON, or no output for commands without a response.
    #>
    [CmdletBinding()]
    [OutputType([object])]
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    $CommandOutput = @(& az @Arguments --only-show-errors --output json)
    if ($LASTEXITCODE -ne 0) {
        # Never include the full argument list: user creation contains a password.
        throw "Azure CLI '$($Arguments[0..2] -join ' ')' failed with exit code $LASTEXITCODE. See the CLI error above."
    }

    if ($CommandOutput.Count -gt 0) {
        ($CommandOutput -join [Environment]::NewLine) | ConvertFrom-Json
    }
}

function Add-EntraGroupMember {
    <#
    .SYNOPSIS
        Add a user or group to a security group only if membership is missing.
    .PARAMETER GroupId
        Object ID of the receiving security group.
    .PARAMETER GroupName
        Group display name for logging.
    .PARAMETER MemberId
        Object ID of the user or child group.
    .PARAMETER MemberName
        User principal name or child group name for logging.
    .OUTPUTS
        None.
    #>
    [CmdletBinding(SupportsShouldProcess)]
    [OutputType([void])]
    param(
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$GroupId,

        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$GroupName,

        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$MemberId,

        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$MemberName
    )

    $Membership = Invoke-AzureCli -Arguments @(
        'ad', 'group', 'member', 'check', '--group', $GroupId, '--member-id', $MemberId
    )
    if ($Membership.value -isnot [bool]) {
        throw "Azure CLI did not return a membership result for '$MemberName' in '$GroupName'."
    }
    if ($Membership.value) {
        Write-Host "'$MemberName' is already a member of security group '$GroupName'."
    }
    elseif ($PSCmdlet.ShouldProcess($MemberName, "Add to security group '$GroupName'")) {
        Invoke-AzureCli -Arguments @(
            'ad', 'group', 'member', 'add', '--group', $GroupId, '--member-id', $MemberId
        ) | Out-Null
        Write-Host "Added '$MemberName' to security group '$GroupName' ($GroupId)."
    }
}

#endregion Functions

#region Main Execution

if ($MyInvocation.InvocationName -ne '.') {
    try {
        if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
            throw 'Azure CLI is not installed or is not on PATH.'
        }

        $Account = Invoke-AzureCli -Arguments @('account', 'show')
        if (-not $Account.tenantId) {
            throw 'No Azure tenant is selected. Run az login --tenant <tenant-id> first.'
        }
        Write-Host "Using tenant $($Account.tenantId)."

        $ParentGroup = $null
        if ($ParentGroupId) {
            $ParentGroup = Invoke-AzureCli -Arguments @('ad', 'group', 'show', '--group', $ParentGroupId)
            if (-not $ParentGroup.id -or -not $ParentGroup.securityEnabled -or $ParentGroup.mailEnabled -or
                $ParentGroup.groupTypes -contains 'DynamicMembership' -or $ParentGroup.isAssignableToRole) {
                throw 'The parent must be an existing non-mail-enabled security group with assigned membership, not a role-assignable group.'
            }
        }
        $Groups = @(Invoke-AzureCli -Arguments @(
            'ad', 'group', 'list', '--filter', "displayName eq '$TeamName'"
        ))
        if ($Groups.Count -gt 1) {
            throw "Multiple groups are named '$TeamName'. Resolve the duplicate names before retrying."
        }
        if ($ParentGroup -and $Groups.Count -eq 1 -and $Groups[0].id -eq $ParentGroup.id) {
            throw 'The team group cannot be its own parent group.'
        }

        $CsvPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputCsvPath)
        $ExportCredentials = $PSCmdlet.ShouldProcess($CsvPath, 'Create plaintext user credentials CSV')
        if (-not $ExportCredentials -and -not $WhatIfPreference) {
            return
        }
        if ($ExportCredentials) {
            # CreateNew protects credentials from earlier runs, including concurrent runs.
            $CsvStream = [System.IO.File]::Open($CsvPath, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write)
            try {
                $Header = [System.Text.Encoding]::UTF8.GetBytes('"DisplayName","UserPrincipalName","Password","Status","Role"' + [Environment]::NewLine)
                $CsvStream.Write($Header, 0, $Header.Length)
            }
            finally {
                $CsvStream.Dispose()
            }
            Write-Warning "Credentials will be saved in plaintext to '$CsvPath'. Protect this file and delete it after secure distribution."
        }

        $Group = $null
        if ($Groups.Count -eq 1) {
            $Group = $Groups[0]
            if (-not $Group.id -or -not $Group.securityEnabled -or $Group.mailEnabled -or
                $Group.groupTypes -contains 'DynamicMembership' -or $Group.isAssignableToRole) {
                throw "The existing group '$TeamName' must be a non-mail-enabled, non-role-assignable security group with assigned membership."
            }
            Write-Host "Reusing security group '$TeamName' ($($Group.id))."
        }
        elseif ($PSCmdlet.ShouldProcess($TeamName, 'Create Entra security group')) {
            $Group = Invoke-AzureCli -Arguments @(
                'ad', 'group', 'create', '--display-name', $TeamName,
                '--mail-nickname', $TeamName,
                '--description', "Workshop security group for $TeamName"
            )
            if (-not $Group.id -or -not $Group.securityEnabled -or $Group.mailEnabled) {
                throw "Azure CLI did not return a non-mail-enabled security group for '$TeamName'."
            }
            Write-Host "Created security group '$TeamName' ($($Group.id))."
        }

        if (-not $Group -and -not $WhatIfPreference) {
            throw 'No team group is available. User creation was not started.'
        }
        if ($ParentGroup) {
            if ($Group) {
                Add-EntraGroupMember -GroupId $ParentGroup.id -GroupName $ParentGroup.displayName `
                    -MemberId $Group.id -MemberName $TeamName
            }
            else {
                $null = $PSCmdlet.ShouldProcess($TeamName, "Add team group to parent security group '$($ParentGroup.displayName)'")
            }
        }

        for ($Index = 0; $Index -le $ParticipantCount; $Index++) {
            if ($Index -eq 0) {
                $Role = 'Coach'
                $UserName = "$($TeamName.ToLowerInvariant())-coach"
                $DisplayName = "$TeamName Coach"
            }
            else {
                $Role = 'Participant'
                $UserName = '{0}-participant{1:D2}' -f $TeamName.ToLowerInvariant(), $Index
                $DisplayName = '{0} Participant {1:D2}' -f $TeamName, $Index
            }
            $Upn = "$UserName@$($TenantDomain.ToLowerInvariant())"
            $Users = @(Invoke-AzureCli -Arguments @(
                'ad', 'user', 'list', '--filter', "userPrincipalName eq '$Upn'"
            ))
            if ($Users.Count -gt 1) {
                throw "Multiple users matched '$Upn'. Resolve the duplicate accounts before retrying."
            }

            $User = $null
            $UserCreated = $false
            if ($Users.Count -eq 1) {
                $User = $Users[0]
                Write-Host "Reusing user '$Upn'; password unchanged."
            }
            elseif ($PSCmdlet.ShouldProcess($Upn, 'Create Entra user requiring password change at first sign-in')) {
                $PlainPassword = New-RandomPassword
                try {
                    $User = Invoke-AzureCli -Arguments @(
                        'ad', 'user', 'create',
                        '--display-name', $DisplayName,
                        '--user-principal-name', $Upn,
                        '--mail-nickname', $UserName,
                        '--password', $PlainPassword,
                        '--force-change-password-next-sign-in', 'true'
                    )
                    if (-not $User.id) {
                        throw "Azure CLI did not return an object ID for '$Upn'."
                    }
                    if ($ExportCredentials) {
                        [pscustomobject]@{
                            DisplayName = $DisplayName
                            UserPrincipalName = $Upn
                            Password = $PlainPassword
                            Status = 'Created'
                            Role = $Role
                        } | Export-Csv -LiteralPath $CsvPath -Append -NoTypeInformation -Encoding utf8
                    }
                    $UserCreated = $true
                }
                finally {
                    $PlainPassword = $null
                }
                Write-Host "Created user '$Upn'."
            }

            if ($User) {
                if (-not $User.id) {
                    throw "Azure CLI did not return an object ID for '$Upn'."
                }
                if (-not $UserCreated -and $ExportCredentials) {
                    [pscustomobject]@{
                        DisplayName = $User.displayName
                        UserPrincipalName = $Upn
                        Password = ''
                        Status = 'Existing'
                        Role = $Role
                    } | Export-Csv -LiteralPath $CsvPath -Append -NoTypeInformation -Encoding utf8
                }
                if ($Group) {
                    Add-EntraGroupMember -GroupId $Group.id -GroupName $TeamName `
                        -MemberId $User.id -MemberName $Upn
                    [pscustomobject]@{
                        TeamName = $TeamName
                        GroupId = $Group.id
                        UserPrincipalName = $Upn
                        UserId = $User.id
                        Role = $Role
                    }
                }
            }
        }
        if ($ExportCredentials) {
            Write-Host "User credentials CSV saved to '$CsvPath'."
        }
    }
    catch {
        Write-Error -ErrorAction Continue "Team provisioning failed: $($_.Exception.Message)"
        exit 1
    }
}

#endregion Main Execution