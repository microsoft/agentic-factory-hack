# Facilitator automation

These scripts are for hackathon organizers, not challenge participants.

## Bulk Entra users

`manage-hack-users.sh` is separate from `provision-hack-environment.sh`.
It creates cloud-only member accounts, assigns a random memorable password, and
adds each account to an **existing assigned security group**. It does not change
Conditional Access policies, assign licenses, or provision Azure resources.

### Prerequisites

- Bash 4+, Azure CLI, jq, OpenSSL, and Git.
- `az login --tenant <tenant-id>` and `az account set --subscription <subscription-id>`.
- An existing verified **managed** domain, such as `yourtenant.onmicrosoft.com`.
  Federated domains and synchronized/dynamic/role-assignable groups are unsupported.
- Permission to create/delete users, read domains/groups/users, and update the
  chosen group's membership. User Administrator is a typical delegated role for
  these non-administrator users/groups; tenant restrictions can require more access.
  App-only automation needs admin-consented Microsoft Graph permissions including
  `User.ReadWrite.All`, `GroupMember.ReadWrite.All`, and `Domain.Read.All`.
- Optional resource-group cleanup requires Azure RBAC permission to read/delete
  the groups in their recorded subscriptions. Entra roles do not grant Azure RBAC.

Use a dedicated workshop tenant and a tightly scoped exclusion group. Membership
does **not** guarantee an MFA exemption: other Conditional Access policies,
security defaults, per-user MFA and registration requirements may still apply.
Existing policy behavior after license expiry does not replace appropriate licensing.

### Create a batch

```bash
./scripts/manage-hack-users.sh create \
  --count 30 \
  --domain yourtenant.onmicrosoft.com \
  --group-name "Hackathon MFA Exclusion"
```

Defaults: users `hack-user-01` through `hack-user-30`, resource-group names
`rg-hack-user-01` through `rg-hack-user-30`, and output
`scripts/.hack-users/users.csv`. Resource groups are **names only** at this stage.

Numbers use a minimum width of two digits: `01`..`09`, `10`..`99`, then
`100` and above without truncation. This applies to custom prefixes, display names,
username collision checks, dry-run output and resource-group names. Existing
accounts are not renamed; cleanup still uses the exact IDs/names in older CSVs.

Passwords use **four randomly selected words and six random digits**, separated by
hyphens. An illustrative format is `Cedar-Moon-River-Peach-482739` (do not reuse this
example). Each word starts with a capital letter; digits can have leading zeroes.
The script uses OpenSSL randomness, a fixed 64-word list and unbiased selection,
providing approximately 44 bits of randomness. This is a convenience tradeoff for
temporary workshop accounts, not a long-lived production password recommendation.
Passwords meet standard Entra complexity categories, but tenant password protection
can still reject a generated password.

By default, accounts **do not require a password change at first sign-in**.
The assigned password is the same value saved in the CSV. Use
`--force-password-change` to opt in, or `--no-force-password-change` to explicitly
keep the default. Tenant policies can still force password resets or MFA/SSPR
registration. Existing accounts/passwords are not modified.

The group name must match exactly. Quote names containing spaces or apostrophes.
Missing or duplicate names cause an error. To disambiguate, use
`--group-id 11111111-2222-3333-4444-555555555555` instead of `--group-name`.

### Check access without creating anything

```bash
./scripts/manage-hack-users.sh create \
  --count 30 \
  --domain yourtenant.onmicrosoft.com \
  --group-name "Hackathon MFA Exclusion" \
  --dry-run
```

This checks the Azure CLI tenant/subscription, resolves and validates the group,
validates the domain, and checks all requested usernames for collisions. It prints
planned usernames and resource-group names. It does not generate passwords, create
users, change membership, create resource groups, or create the CSV/output directory.
Temporary diagnostic files are removed on exit. Domain-read failures show the
underlying CLI error and permission guidance; credential-bearing write diagnostics
are suppressed to avoid leaking passwords.

A successful dry run proves **read access only**, not permission to create/delete
users or change membership. CSV output-path readiness is not checked. Existing CSVs
are still rejected to preserve no-overwrite behavior for creation. Remove
`--dry-run` to provision the batch. Cleanup also supports dry run, as shown below.

Override naming and use a separate file for another batch:

```bash
./scripts/manage-hack-users.sh create \
  --count 10 --start 31 --prefix factory-user --resource-group-prefix rg-workshop \
  --domain yourtenant.onmicrosoft.com \
  --group-name "Hackathon MFA Exclusion" \
  --csv scripts/.hack-users/batch-2.csv
```

The CSV has six columns:

```text
username,password,resource_group,user_id,tenant_id,subscription_id
```

Fields are unquoted and cannot contain commas, quotes or newlines. Keep the
original manifest unchanged for cleanup. Export a separate participant copy if
needed. **Passwords are plaintext credentials**: distribute individually and
securely, never share the entire file, and remove local copies after cleanup.
The directory is Git-ignored and the CSV is created with owner-only mode `600`.
Custom output paths inside the repository must be ignored and untracked. You are
responsible for paths outside the repository, backups and copies; Git ignore
cannot protect files deliberately force-added.

Creation preflights username collisions and never adopts existing users or resets
their passwords. Each account and password are recorded **before group assignment**.
If assignment fails, the script stops with a nonzero exit and the manifest still
supports cleanup. Do not assume every recorded user is in the exclusion group.
Write operations are not automatically retried. A network timeout during creation
can leave an account without a returned ID; inspect Entra for the attempted username
before rerunning. Cleanup supports the earlier six-column password CSV formats.

### Provision environments later

For the generated manifest, this loop passes only usernames and group names:

```bash
CSV=scripts/.hack-users/users.csv
(
  set -euo pipefail
  while IFS=, read -r username password resource_group user_id tenant_id subscription_id; do
    az account set --subscription "$subscription_id"
    bash scripts/provision-hack-environment.sh \
      -u "$username" -g "$resource_group" -l swedencentral </dev/null
  done < <(tail -n +2 "$CSV")
)
```

Allow directory/membership replication before sign-in or role assignments. Test
with one participant before the event. The existing environment provisioning
script's permissions, deployment behavior and ownership tags are unchanged.
User lookup failures display the original Azure CLI error; a lookup failure does
not necessarily mean the account is absent (authentication or Graph access can fail).

The infrastructure template serializes model deployments, the account Search
connection, project creation and the project connection under the Foundry account.
The project Application Insights connection uses the full Application Insights
connection string; an instrumentation key alone is not supported.
Parallel account-child writes can cause `RequestConflict` even in a fresh resource
group. If deployment fails, the script prints the failed resource operations.
Wait for any in-flight account operation to finish, then rerun against the **same**
resource group; a failed deployment can leave successfully created, billable resources.
Avoid concurrent provisioning runs for the same group. If you create a differently
named group, the user CSV is not automatically updated and cleanup will only target
the original recorded group.

Resource names now use a stable suffix derived from the resource-group ID, not
the timestamped deployment name. For existing environments, the script discovers
and reuses the suffix from existing `msagthack` resource names, including partially
deployed environments. Resource-list failures stop provisioning rather than guess.
The script requires jq for this check.

Earlier template versions included the deployment name in the suffix, so retries
could create additional resource sets. If multiple suffixes are found, the script
stops **before assigning roles or deploying resources**. Inspect the resources and
select the environment to update explicitly:

```bash
bash scripts/provision-hack-environment.sh \
  -u hack-user-03@yourtenant.onmicrosoft.com \
  -g rg-hack-user-03 -l francecentral \
  --resource-suffix gxq25v7mhxsdw
```

Use the actual suffix from your resource inventory. This does not remove other
resource sets, release their quota, or migrate their data. Review and clean up
unneeded resources separately. When deploying the ARM template directly to an
existing environment, explicitly pass `resourceSuffix=<existing-suffix>` as well;
the template alone does not discover existing names. Changing that parameter
creates different resources rather than renaming the existing ones.

### Remove the batch

Review the original manifest before confirming:

```bash
# Preview users only; --yes is not needed for dry run.
./scripts/manage-hack-users.sh delete --dry-run

# Preview users plus resource groups, validating ownership tags.
./scripts/manage-hack-users.sh delete --delete-resource-groups --dry-run

# Users only; Azure resources are retained.
./scripts/manage-hack-users.sh delete --yes

# Users and ALL resources in their recorded resource groups.
./scripts/manage-hack-users.sh delete --delete-resource-groups --yes

# Separately named batch.
./scripts/manage-hack-users.sh delete --csv scripts/.hack-users/batch-2.csv --yes
```

Add `--csv scripts/.hack-users/batch-2.csv` to preview a custom manifest.
Delete dry-run validates the same CSV, tenant, existing user identities and optional
resource-group tags as real cleanup. It prints what would be deleted and skips
already absent users/groups without changing cloud resources or the CSV. It checks
read access, **not deletion permissions**. Even if `--yes` is supplied together with
`--dry-run`, nothing is deleted. To delete, remove `--dry-run` and supply `--yes`.

Cleanup accepts LF or Windows CRLF line endings and an optional UTF-8 BOM, including
Excel's CSV UTF-8 export. Keep the original six comma-separated columns and their
header names unchanged; semicolon-delimited and UTF-16 exports are not supported.
The manifest is not rewritten during cleanup.

Cleanup validates every row before deleting, requires a matching login tenant,
and uses recorded object IDs rather than prefix-based deletion. Existing usernames
must match their IDs. Optional group deletion uses the recorded subscription and
requires `hackuser=<username>`, `environment=hack`, and `createdBy=automation` tags
set by `provision-hack-environment.sh`. Do not retag/reuse workshop resource groups.
Protect the manifest from modification; it is not an authorization boundary.

Resource-group deletion is requested before deleting each corresponding user,
using `az group delete --no-wait`. Cleanup continues without waiting for the
resource groups to finish deleting, so multiple deletions can proceed in Azure
at once. Request errors stop cleanup; resolve the cause and rerun with the same CSV.
Completion and later deletion failures are not checked; check Azure for final status.
Already absent users/groups are skipped. Entra deletion is soft deletion and
removes group memberships. The CSV is retained for audit/retry; securely remove
it when no longer needed.

### Offline verification

```bash
bash -n scripts/manage-hack-users.sh
bash -n scripts/provision-hack-environment.sh
node --test scripts/manage-hack-users.test.cjs scripts/provision-hack-environment.test.cjs
```

Tests use Node's built-in test runner and a mocked Azure CLI. They do not access
Entra or create real resources. Node is required only for tests.

References:
- [Create a Microsoft Graph user](https://learn.microsoft.com/en-us/graph/api/user-post-users?view=graph-rest-1.0)
- [Add group members](https://learn.microsoft.com/en-us/graph/api/group-post-members?view=graph-rest-1.0)
- [Conditional Access exclusions](https://learn.microsoft.com/en-us/entra/identity/conditional-access/concept-conditional-access-users-groups#exclude-users)
