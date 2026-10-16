---
title: Create workshop team users
description: Create an Entra team group, a coach, participants, and optional parent-group membership.
---

## Prerequisites

* PowerShell 7 or later and Azure CLI
* A verified, managed domain in the target Microsoft Entra tenant
* Directory permissions to create users, security groups, and memberships,
  such as the User Administrator role

Azure subscription Owner or Contributor access alone does not grant these
directory permissions. Federated domains are not supported by this script.

## Create a team

Run these commands from the repository root:

```powershell
az login --tenant <tenant-id>
.\challenge-0\users\create-users.ps1 `
    -TeamName team-blue `
    -ParticipantCount 5 `
    -TenantDomain contoso.onmicrosoft.com
```

The script creates a non-mail-enabled security group named `team-blue` and
one coach account `team-blue-coach@contoso.onmicrosoft.com` plus participants
`team-blue-participant01@contoso.onmicrosoft.com` through
`team-blue-participant05@contoso.onmicrosoft.com`. Their display names are
`team-blue Coach` and `team-blue Participant 01` through `team-blue Participant 05`.
All six users are added to the team group.

Team names accept 1-40 letters, digits, or hyphens and must start with a letter
or digit. `ParticipantCount` accepts 1-999 and excludes the coach. `UserCount`
remains an alias, but now also counts participants only.

Coach and participant are workshop labels, not Entra administrative roles.
Both are ordinary group members; the coach is not automatically a group owner.
Accounts using the previous `-user001` naming scheme are not renamed or deleted.

Each new account receives an independently generated, cryptographically random
20-character temporary password containing uppercase letters, lowercase letters,
digits, and symbols. Users must change it at first sign-in. Passwords are saved
in the credentials CSV for secure distribution.

No password prompt or `-InitialPassword` parameter is used. Existing accounts'
passwords remain unchanged. Your tenant's custom password policies may reject a
generated password; the script reports the CLI error rather than bypassing policy.

> [!WARNING]
> Azure CLI requires the password as a process argument. Run on a trusted host
> where other users cannot inspect your processes. The CSV contains plaintext
> passwords. Restrict access, distribute credentials securely, and delete the
> file afterward. The script does not print passwords to the console.

## Credentials CSV

Each run creates a timestamped `<TeamName>-user-credentials-<timestamp>.csv`
beside [create-users.ps1](./create-users.ps1). Choose a different location with
`-OutputCsvPath`:

```powershell
.\challenge-0\users\create-users.ps1 `
    -TeamName team-blue -ParticipantCount 5 `
    -TenantDomain contoso.onmicrosoft.com `
    -OutputCsvPath "$HOME\team-blue-user-credentials.csv"
```

The CSV columns are `DisplayName`, `UserPrincipalName`, `Password`, `Status`,
and `Role` (`Coach` or `Participant`).
New accounts have status `Created` and their temporary password. Existing
accounts have status `Existing` and a blank password because Entra passwords
cannot be retrieved; their passwords are not reset.

The parent directory must exist. Existing files are never overwritten.
Rows are saved as users are processed, before membership changes, so credentials
for newly created accounts remain available if a later CLI operation fails.
The CSV does not indicate whether group membership succeeded.

Default credential filenames are ignored by Git. If you choose a custom filename
inside the repository, ensure it is ignored too. Never commit credentials.

## Preview and verify

Add `-WhatIf` to preview changes without creating users, groups, memberships,
or a CSV file.
Preview still requires login and directory read permissions, but does not
generate passwords.

```powershell
.\challenge-0\users\create-users.ps1 `
    -TeamName team-blue -ParticipantCount 5 `
    -TenantDomain contoso.onmicrosoft.com -WhatIf

az ad group show --group team-blue --query "{id:id,securityEnabled:securityEnabled,mailEnabled:mailEnabled}"
az ad group member list --group team-blue --query "[].userPrincipalName" --output table
```

Successful runs output group and user IDs without passwords. Repeated runs reuse
the exact matching group and accounts and add missing memberships. They do not
reset passwords, remove extra members, or delete accounts when you reduce the
count. Use a unique team name to avoid adding unrelated existing accounts.

Every new or reused user is checked for membership in the team's security group.
The script adds missing members using their object IDs and reports each
successful addition or existing membership in the console.

The script stops on CLI errors and returns exit code 1. Previously completed
changes remain in place; resolve the error and rerun with the same parameters.
No licenses or Azure resource roles are assigned.

## Optional parent group

Use `-ParentGroupId` to add the team security group as a member of an existing
parent security group. This nests the team group; it does not add the parent to
the team or make it an owner.

```powershell
.\challenge-0\users\create-users.ps1 `
    -TeamName team-blue -ParticipantCount 5 `
    -TenantDomain contoso.onmicrosoft.com `
    -ParentGroupId '11111111-2222-3333-4444-555555555555'
```

The parent must be a non-mail-enabled security group with assigned membership.
Dynamic groups, role-assignable groups, and using the team itself as its parent
are rejected. You need permission to update the parent's membership as well.
Existing parent membership is preserved on reruns. Omitting the parameter leaves
parent membership unchanged.

Verify the team group's direct membership in the parent:

```powershell
$TeamGroupId = az ad group show --group team-blue --query id --output tsv
az ad group member check `
    --group '11111111-2222-3333-4444-555555555555' `
    --member-id $TeamGroupId
```

Parent nesting is performed before user creation. If it fails, the script stops
and reports the CLI error. Nested membership does not guarantee that every
application or Azure authorization feature supports inherited membership.
