#!/usr/bin/env bash
# Facilitator-only lifecycle management. Never run with shell tracing enabled.
set +x
set -euo pipefail
umask 077

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
HEADER="username,password,resource_group,user_id,tenant_id,subscription_id"
UUID='^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$'
CSV="$SCRIPT_DIR/.hack-users/users.csv"
PREFIX="hack-user"
RG_PREFIX="rg"
START=1
COUNT=""
DOMAIN=""
GROUP_ID=""
GROUP_NAME=""
DRY_RUN=false
DELETE_RGS=false
CONFIRMED=false
FORCE_CHANGE=false

usage() {
    cat <<'EOF'
Usage:
  bash scripts/manage-hack-users.sh create --count N --domain DOMAIN --group-name NAME [options]
  bash scripts/manage-hack-users.sh delete [--csv PATH] [--delete-resource-groups] (--dry-run | --yes)

Create options:
  --count N                 Number of users to create (required; maximum 10000)
  --domain DOMAIN           Verified, managed Entra domain (required)
  --group-name NAME         Existing assigned security group's exact display name
  --group-id UUID           Alternative to --group-name; specify exactly one
  --prefix PREFIX           User prefix (default: hack-user; produces hack-user-01)
  --start N                 First user number (default: 1)
  --resource-group-prefix P Resource-group prefix (default: rg; produces rg-hack-user-01)
  --force-password-change   Require password change at first sign-in (default: off)
  --no-force-password-change
                            Keep the CSV password at first sign-in (default)

Common options:
  --csv PATH                CSV manifest (default: scripts/.hack-users/users.csv)
  --dry-run                 Validate and preview create/delete; no cloud changes or CSV writes
  --help                    Show help

Delete options:
  --yes                     Required for real deletion; deletes ONLY manifest user IDs
  --delete-resource-groups   Request deletion of tagged resource groups without waiting

Requires Bash 4+, Azure CLI, jq, OpenSSL, Git, and an authorized `az login`.
The CSV contains plaintext passwords. Existing files are never overwritten.
Creation records resource-group names but does NOT provision resource groups.
EOF
}

die() { printf 'ERROR: %s\n' "$*" >&2; exit 1; }
info() { printf '%s\n' "$*" >&2; }
need_value() { [[ $# -ge 2 && -n "$2" && "$2" != --* ]] || die "$1 needs a value"; }

ACTION="${1:---help}"
[[ "$ACTION" != --help && "$ACTION" != -h ]] || { usage; exit 0; }
[[ "$ACTION" == create || "$ACTION" == delete ]] || die "Expected create or delete. Use --help."
shift
while (($#)); do
    case "$1" in
        --csv) need_value "$@"; CSV="$2"; shift 2 ;;
        --count|--domain|--group-id|--group-name|--prefix|--start|--resource-group-prefix)
            [[ "$ACTION" == create ]] || die "$1 is only valid for create"
            need_value "$@"
            case "$1" in
                --count) COUNT="$2" ;;
                --domain) DOMAIN="$2" ;;
                --group-id) GROUP_ID="$2" ;;
                --group-name) GROUP_NAME="$2" ;;
                --prefix) PREFIX="$2" ;;
                --start) START="$2" ;;
                --resource-group-prefix) RG_PREFIX="$2" ;;
            esac
            shift 2 ;;
        --dry-run)
            DRY_RUN=true; shift ;;
        --force-password-change|--no-force-password-change)
            [[ "$ACTION" == create ]] || die "$1 is only valid for create"
            if [[ "$1" == --force-password-change ]]; then FORCE_CHANGE=true; else FORCE_CHANGE=false; fi
            shift ;;
        --delete-resource-groups|--yes)
            [[ "$ACTION" == delete ]] || die "$1 is only valid for delete"
            if [[ "$1" == --yes ]]; then CONFIRMED=true; else DELETE_RGS=true; fi
            shift ;;
        --help|-h) usage; exit 0 ;;
        *) die "Unknown option: $1" ;;
    esac
done

for tool in az jq openssl git; do
    command -v "$tool" >/dev/null || die "Required tool not found: $tool"
done
[[ "$CSV" == *.csv ]] || die "--csv must end in .csv"

if [[ "$ACTION" == create ]]; then
    [[ "$COUNT" =~ ^[1-9][0-9]{0,4}$ ]] && ((COUNT <= 10000)) || die "--count must be 1..10000"
    [[ "$START" =~ ^[1-9][0-9]{0,5}$ ]] || die "--start must be 1..999999"
    [[ "$PREFIX" =~ ^[a-zA-Z][a-zA-Z0-9-]{0,39}$ ]] || die "Invalid user prefix (1..40 alphanumeric/hyphen characters, starting with a letter)"
    [[ "$RG_PREFIX" =~ ^[a-zA-Z][a-zA-Z0-9-]{0,19}$ ]] || die "Invalid resource-group prefix (1..20 alphanumeric/hyphen characters, starting with a letter)"
    [[ "$DOMAIN" =~ ^[a-zA-Z0-9][a-zA-Z0-9.-]*\.[a-zA-Z0-9-]+$ ]] || die "Specify a verified Entra --domain"
    if [[ -n "$GROUP_NAME" ]]; then
        [[ -z "$GROUP_ID" ]] || die "Specify only one of --group-name or --group-id"
        [[ "$GROUP_NAME" != *$'\n'* && "$GROUP_NAME" != *$'\r'* ]] || die "Group name cannot contain newlines"
    else
        [[ "$GROUP_ID" =~ $UUID ]] || die "Specify --group-name or a valid --group-id object UUID"
    fi
    [[ ! -e "$CSV" && ! -L "$CSV" ]] || die "CSV already exists; use another --csv and --start for another batch"
else
    [[ "$CONFIRMED" == true || "$DRY_RUN" == true ]] || die "Deletion requires --yes, or use --dry-run to preview. Review the CSV first."
    [[ -f "$CSV" && ! -L "$CSV" ]] || die "CSV is missing or is a symbolic link"
fi

TMP_DIR="$(mktemp -d)"
cleanup() {
    rm -f -- "$TMP_DIR/request.json" "$TMP_DIR/error.log"
    rmdir -- "$TMP_DIR"
}
trap cleanup EXIT

# Do not echo CLI failures that might include a request body containing a password.
az_safe() {
    if ! az "$@" --only-show-errors 2>"$TMP_DIR/error.log"; then
        die "Azure CLI operation '$1 ${2:-}' failed. Check login, permissions and connectivity. No automatic retry was made; review the manifest and tenant before rerunning."
    fi
}

# Only use for non-credential domain reads.
graph_preflight_read() {
    local label="$1" url="$2" hint="$3"
    info "Checking $label..."
    if ! az rest --method get --url "$url" --output json --only-show-errors 2>"$TMP_DIR/error.log"; then
        printf 'ERROR: Cannot read %s.\n' "$label" >&2
        cat "$TMP_DIR/error.log" >&2
        die "$hint No users or credentials were created."
    fi
}

generate_password() {
    local -a words=(
        Apple Beach Birch Bloom Brook Cedar Cloud Coral
        Daisy Dawn Dove Dune Eagle Earth Elm Fern
        Finch Flame Flint Frost Grove Hazel Hill Ivy
        Jade Lake Leaf Lemon Lily Lime Maple Meadow
        Mint Moon Moss Oak Ocean Olive Opal Palm
        Panda Peach Pearl Pine Plum Pond Rain Reed
        River Robin Rock Rose Ruby Sage Sand Shell
        Sky Snow Star Stone Sunny Swift Tiger Tulip
        Valley Wave Willow Wind Wolf Wood Wren Zebra
    )
    local bytes index number phrase="" j
    bytes="$(openssl rand -hex 4)" || die "Cannot generate random password words"
    for ((j=0; j<8; j+=2)); do
        index=$((16#${bytes:j:2} % 64))
        phrase+="${words[index]}-"
    done
    # Reject the uneven tail so every six-digit suffix is equally likely.
    while true; do
        bytes="$(openssl rand -hex 3)" || die "Cannot generate random password digits"
        number=$((16#$bytes))
        if ((number < 16000000)); then break; fi
    done
    printf '%s%06d' "$phrase" "$((number % 1000000))"
}

ACCOUNT="$(az_safe account show --output json)"
TENANT_ID="$(jq -er '.tenantId' <<<"$ACCOUNT")"
SUBSCRIPTION_ID="$(jq -er '.id' <<<"$ACCOUNT")"
[[ "$TENANT_ID" =~ $UUID && "$SUBSCRIPTION_ID" =~ $UUID ]] || die "Login must have a valid tenant and subscription"
info "Tenant: $TENANT_ID; subscription: $SUBSCRIPTION_ID"

if [[ "$ACTION" == create ]]; then
    if [[ -n "$GROUP_NAME" ]]; then
        # OData string literals escape apostrophes by doubling them.
        ESCAPED_GROUP_NAME="${GROUP_NAME//\'/\'\'}"
        GROUPS_JSON="$(az_safe ad group list --filter "displayName eq '$ESCAPED_GROUP_NAME'" --output json)"
        EXACT_GROUPS="$(jq --arg name "$GROUP_NAME" '[.[] | select(.displayName == $name)]' <<<"$GROUPS_JSON")"
        GROUP_COUNT="$(jq -er 'length' <<<"$EXACT_GROUPS")"
        [[ "$GROUP_COUNT" != 0 ]] || die "No group with exact display name: $GROUP_NAME"
        [[ "$GROUP_COUNT" == 1 ]] || die "Multiple groups have display name '$GROUP_NAME'; use --group-id to disambiguate"
        GROUP_ID="$(jq -er '.[0].id' <<<"$EXACT_GROUPS")"
        [[ "$GROUP_ID" =~ $UUID ]] || die "Group lookup returned an invalid object ID"
    fi
    GROUP="$(az_safe ad group show --group "$GROUP_ID" --output json)"
    jq -e '.securityEnabled == true and .isAssignableToRole != true and
        .onPremisesSyncEnabled != true and ((.groupTypes // []) | index("DynamicMembership") | not)' \
        <<<"$GROUP" >/dev/null || die "Use a cloud-managed, assigned security group without role-assignable membership"
    info "Validated exclusion group: ${GROUP_NAME:-$GROUP_ID} ($GROUP_ID)"
    DOMAINS="$(graph_preflight_read "verified Entra domains" 'https://graph.microsoft.com/v1.0/domains' \
        "Verify Graph Domain.Read.All permission and the signed-in tenant.")"
    jq -e --arg domain "$DOMAIN" '.value[] | select(
        (.id | ascii_downcase) == ($domain | ascii_downcase) and
        .isVerified == true and .authenticationType == "Managed")' \
        <<<"$DOMAINS" >/dev/null || die "Domain must be verified and managed in the current tenant"

    # Preflight the complete batch; never reset passwords or adopt existing accounts.
    for ((i=START; i<START+COUNT; i++)); do
        printf -v NAME '%s-%02d' "$PREFIX" "$i"
        USERNAME="$NAME@$DOMAIN"
        EXISTING="$(az_safe ad user list --filter "userPrincipalName eq '$USERNAME'" --output json)"
        [[ "$(jq -er 'length' <<<"$EXISTING")" == 0 ]] || die "User already exists: $USERNAME"
    done

    if [[ "$DRY_RUN" == true ]]; then
        info "DRY RUN: Entra read access, group, domain and $COUNT available usernames validated."
        for ((i=START; i<START+COUNT; i++)); do
            printf -v NAME '%s-%02d' "$PREFIX" "$i"
            info "Would create $NAME@$DOMAIN, add to group $GROUP_ID, and record resource group $RG_PREFIX-$NAME"
        done
        info "No users, memberships, passwords, resource groups or CSV created. Write permissions and CSV output-path readiness are not verified."
        exit 0
    fi

    mkdir -p "$(dirname "$CSV")"
    CSV="$(cd "$(dirname "$CSV")" && pwd)/$(basename "$CSV")"
    if [[ "$CSV" == "$REPO_ROOT/"* ]]; then
        git -C "$REPO_ROOT" check-ignore --quiet -- "$CSV" ||
            die "CSV path must be Git-ignored and untracked. Use scripts/.hack-users/."
    fi
    (set -o noclobber; printf '%s\n' "$HEADER" >"$CSV") || die "Cannot exclusively create CSV"
    chmod 600 "$CSV"
    for ((i=START; i<START+COUNT; i++)); do
        printf -v NAME '%s-%02d' "$PREFIX" "$i"
        USERNAME="$NAME@$DOMAIN"
        RESOURCE_GROUP="$RG_PREFIX-$NAME"
        PASSWORD="$(generate_password)"
        # Pass secrets through a private file and stdin, not process arguments.
        printf '%s' "$PASSWORD" | jq -Rs \
            --arg name "$NAME" --arg upn "$USERNAME" --argjson force "$FORCE_CHANGE" \
            '{accountEnabled: true, displayName: $name, mailNickname: $name,
              userPrincipalName: $upn, passwordProfile: {
                password: ., forceChangePasswordNextSignIn: $force}}' >"$TMP_DIR/request.json"
        info "Creating $USERNAME"
        CREATED="$(az_safe rest --method post --url 'https://graph.microsoft.com/v1.0/users' \
            --headers Content-Type=application/json --body "@$TMP_DIR/request.json" --output json)"
        USER_ID="$(jq -er '.id' <<<"$CREATED")"
        [[ "$USER_ID" =~ $UUID ]] || die "User creation returned an invalid ID; inspect the tenant for $USERNAME"
        # Record before group assignment so failed assignments can still be cleaned up.
        printf '%s,%s,%s,%s,%s,%s\n' "$USERNAME" "$PASSWORD" "$RESOURCE_GROUP" \
            "$USER_ID" "$TENANT_ID" "$SUBSCRIPTION_ID" >>"$CSV"
        rm -f -- "$TMP_DIR/request.json"
        unset PASSWORD CREATED
        az_safe ad group member add --group "$GROUP_ID" --member-id "$USER_ID" --output none
        info "Created and added to exclusion group: $USERNAME (resource group: $RESOURCE_GROUP)"
    done
    info "Saved $COUNT users to $CSV (permissions 600). Resource groups have not been created."
else
    mapfile -t ROWS <"$CSV"
    for ((ROW_INDEX=0; ROW_INDEX<${#ROWS[@]}; ROW_INDEX++)); do
        ROWS[ROW_INDEX]="${ROWS[ROW_INDEX]%$'\r'}"
    done
    if ((${#ROWS[@]} > 0)); then
        ROWS[0]="${ROWS[0]#$'\xef\xbb\xbf'}"
    fi
    [[ "${ROWS[0]:-}" == "$HEADER" ]] ||
        die "Unrecognized CSV header; use the original generated manifest"
    ((${#ROWS[@]} > 1)) || die "CSV has no users"

    # Validate EVERY row before issuing any destructive command.
    declare -A SEEN_USERS=()
    declare -A SEEN_RGS=()
    for ROW in "${ROWS[@]:1}"; do
        [[ "${ROW//[^,]/}" == ",,,,," ]] || die "Malformed CSV row"
        IFS=, read -r USERNAME PASSWORD RESOURCE_GROUP USER_ID ROW_TENANT ROW_SUB EXTRA <<<"$ROW"
        [[ "$PASSWORD" =~ ^Aa1![0-9a-f]{32}$ || "$PASSWORD" =~ ^Ha7(-[A-HJ-NP-Z2-9]{4}){3}$ ||
           "$PASSWORD" =~ ^([A-Z][a-z]{2,5}-){4}[0-9]{6}$ ]] || die "Invalid password field"
        [[ "$ROW" != *, && "$ROW" != *$'\r'* && "$ROW" != *'"'* && -z "$EXTRA" ]] ||
            die "Malformed CSV row"
        [[ "$USERNAME" =~ ^[a-zA-Z][a-zA-Z0-9-]*@[a-zA-Z0-9.-]+$ &&
           "$RESOURCE_GROUP" =~ ^[a-zA-Z][a-zA-Z0-9-]{0,89}$ &&
           "$USER_ID" =~ $UUID && "$ROW_TENANT" == "$TENANT_ID" && "$ROW_SUB" =~ $UUID ]] ||
            die "Invalid CSV fields or tenant mismatch"
        [[ -z "${SEEN_USERS[$USER_ID]:-}" && -z "${SEEN_RGS[$ROW_SUB/$RESOURCE_GROUP]:-}" ]] ||
            die "Duplicate user ID or resource group in CSV"
        SEEN_USERS[$USER_ID]=1
        SEEN_RGS[$ROW_SUB/$RESOURCE_GROUP]=1
    done
    unset PASSWORD

    for ROW in "${ROWS[@]:1}"; do
        IFS=, read -r USERNAME PASSWORD RESOURCE_GROUP USER_ID ROW_TENANT ROW_SUB <<<"$ROW"
        USERS="$(az_safe ad user list --filter "id eq '$USER_ID'" --output json)"
        MATCHES="$(jq -er 'length' <<<"$USERS")"
        if [[ "$MATCHES" == 1 ]]; then
            ACTUAL_UPN="$(jq -er '.[0].userPrincipalName' <<<"$USERS")"
            [[ "${ACTUAL_UPN,,}" == "${USERNAME,,}" ]] || die "User ID/username mismatch for $USERNAME"
        elif [[ "$MATCHES" != 0 ]]; then
            die "Unexpected user lookup result for $USERNAME"
        fi
        if [[ "$DELETE_RGS" == true ]]; then
            EXISTS="$(az_safe group exists --name "$RESOURCE_GROUP" --subscription "$ROW_SUB" --output json)"
            if [[ "$EXISTS" == true ]]; then
                RG="$(az_safe group show --name "$RESOURCE_GROUP" --subscription "$ROW_SUB" --output json)"
                jq -e --arg upn "$USERNAME" '.tags.hackuser == $upn and
                    .tags.environment == "hack" and .tags.createdBy == "automation"' \
                    <<<"$RG" >/dev/null || die "Refusing to delete $RESOURCE_GROUP: expected workshop ownership tags are missing or different"
                if [[ "$DRY_RUN" == true ]]; then
                    info "DRY RUN: Would delete resource group $RESOURCE_GROUP in subscription $ROW_SUB and ALL resources inside it (without waiting for completion)"
                else
                    info "Requesting deletion of resource group $RESOURCE_GROUP in subscription $ROW_SUB (without waiting for completion)"
                    az_safe group delete --name "$RESOURCE_GROUP" --subscription "$ROW_SUB" --yes --no-wait --output none
                fi
            elif [[ "$EXISTS" == false ]]; then
                info "Resource group already absent: $RESOURCE_GROUP"
            else
                die "Unexpected resource-group lookup result"
            fi
        fi
        if [[ "$MATCHES" == 1 ]]; then
            if [[ "$DRY_RUN" == true ]]; then
                info "DRY RUN: Would delete user $USERNAME ($USER_ID)"
            else
                info "Deleting user $USERNAME ($USER_ID)"
                az_safe ad user delete --id "$USER_ID" --output none
            fi
        else
            info "User already absent: $USERNAME ($USER_ID)"
        fi
    done
    if [[ "$DRY_RUN" == true ]]; then
        info "DRY RUN complete. No users or resource groups deleted; CSV unchanged. Read access was checked, not delete permissions."
    else
        if [[ "$DELETE_RGS" == true ]]; then
            info "Resource-group deletion requests submitted; completion is not verified. Check Azure for final status."
        fi
        info "Cleanup complete. Entra users are soft-deleted; the sensitive CSV is retained for audit/retry."
    fi
fi
