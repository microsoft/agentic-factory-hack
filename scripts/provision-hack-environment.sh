#!/bin/bash
#
# Provision a test resource group for a workshop user
# Usage: ./provision-hack-environment.sh -u <username> -g <resource-group> -l <location>
#
# NOTE: This script is NOT intended to be used as part of the hack challenges.
# It is only meant for workshop facilitators/admins to automate the creation
# of hack environments for participants.
#

set -e

# Default values
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
TEMPLATE_FILE="$REPO_ROOT/challenge-0/infra/azuredeploy.json"
ADDITIONAL_TAGS=""
RESOURCE_SUFFIX=""

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

usage() {
    echo "Usage: $0 -u <username> -g <resource-group> -l <location> [-t <tags>] [--resource-suffix <suffix>]"
    echo ""
    echo "NOTE: This script is for workshop facilitators only, not for hack participants."
    echo ""
    echo "Required parameters:"
    echo "  -u, --username        Azure AD username (email) to assign permissions to"
    echo "  -g, --resource-group  Name of the resource group to create"
    echo "  -l, --location        Azure region (swedencentral, francecentral, germanywestcentral, eastus2)"
    echo ""
    echo "Optional parameters:"
    echo "  -t, --tags            Additional tags in format 'key1=value1 key2=value2'"
    echo "  --resource-suffix     Existing 13-character suffix to reuse when multiple environments are found"
    echo "  -h, --help            Show this help message"
    echo ""
    echo "Example:"
    echo "  $0 -u user@contoso.com -g rg-hack-user1 -l swedencentral"
    echo "  $0 -u user@contoso.com -g rg-hack-user1 -l swedencentral -t 'team=alpha costcenter=12345'"
    exit 1
}

log_info() {
    echo -e "${BLUE}ℹ️  $1${NC}"
}

log_success() {
    echo -e "${GREEN}✅ $1${NC}"
}

log_warning() {
    echo -e "${YELLOW}⚠️  $1${NC}"
}

log_error() {
    echo -e "${RED}❌ $1${NC}"
}

# Parse command line arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        -u|--username)
            USERNAME="$2"
            shift 2
            ;;
        -g|--resource-group)
            RESOURCE_GROUP="$2"
            shift 2
            ;;
        -l|--location)
            LOCATION="$2"
            shift 2
            ;;
        -t|--tags)
            ADDITIONAL_TAGS="$2"
            shift 2
            ;;
        -h|--help)
            usage
            ;;
        --resource-suffix)
            if [[ $# -lt 2 || ! "$2" =~ ^[a-z0-9]{13}$ ]]; then
                log_error "--resource-suffix requires exactly 13 lowercase letters or digits"
                exit 1
            fi
            RESOURCE_SUFFIX="$2"
            shift 2
            ;;
        *)
            echo "Unknown option: $1"
            usage
            ;;
    esac
done

# Validate required parameters
if [ -z "$USERNAME" ] || [ -z "$RESOURCE_GROUP" ] || [ -z "$LOCATION" ]; then
    log_error "Missing required parameters"
    usage
fi

# Validate location
if [[ ! "$LOCATION" =~ ^(swedencentral|francecentral|germanywestcentral|eastus2)$ ]]; then
    log_error "Invalid location: $LOCATION"
    echo "Allowed values: swedencentral, francecentral, germanywestcentral, eastus2"
    exit 1
fi

# Check if template file exists
if [ ! -f "$TEMPLATE_FILE" ]; then
    log_error "Template file not found: $TEMPLATE_FILE"
    exit 1
fi
if ! command -v jq >/dev/null 2>&1; then
    log_error "jq is required to discover existing resource suffixes."
    exit 1
fi

# Check if logged into Azure
log_info "Checking Azure CLI login status..."
if ! az account show &>/dev/null; then
    log_error "Not logged into Azure CLI. Please run 'az login' first."
    exit 1
fi

# Get subscription info
SUBSCRIPTION_ID=$(az account show --query id -o tsv)
SUBSCRIPTION_NAME=$(az account show --query name -o tsv)
log_info "Using subscription: $SUBSCRIPTION_NAME ($SUBSCRIPTION_ID)"

# Get user's Object ID
log_info "Looking up user: $USERNAME"
USER_ID=$(az ad user show --id "$USERNAME" --query id -o tsv --only-show-errors) || {
    log_error "Could not look up user: $USERNAME"
    echo "See the Azure CLI error above. Check the username, current tenant, Graph permissions, and login session."
    exit 1
}
if [[ -z "$USER_ID" || "$USER_ID" == "null" || "$USER_ID" == "None" ]]; then
    log_error "User lookup returned no object ID for: $USERNAME"
    exit 1
fi
log_success "Found user with Object ID: $USER_ID"

# Check if resource group already exists
GROUP_EXISTS=$(az group exists --name "$RESOURCE_GROUP" --subscription "$SUBSCRIPTION_ID") || {
    log_error "Could not check whether resource group $RESOURCE_GROUP exists."
    exit 1
}
if [[ "$GROUP_EXISTS" != "true" && "$GROUP_EXISTS" != "false" ]]; then
    log_error "Unexpected resource-group existence response: $GROUP_EXISTS"
    exit 1
fi
if [[ "$GROUP_EXISTS" == "true" ]]; then
    EXISTING_RESOURCES=$(az resource list \
        --resource-group "$RESOURCE_GROUP" \
        --subscription "$SUBSCRIPTION_ID" \
        --query "[].name" --output json) || {
        log_error "Could not list existing resources; refusing to guess a resource suffix."
        exit 1
    }
    EXISTING_SUFFIXES=$(jq -r '
        if type != "array" or any(.[]; type != "string") then
            error("Expected an array of resource names")
        else
            [.[] | select(test("^msagthack(-.*-|cr|sa)[a-z0-9]{13}$"))
             | capture("(?<suffix>[a-z0-9]{13})$").suffix]
            | unique | .[]
        end
    ' <<< "$EXISTING_RESOURCES") || {
        log_error "Could not parse existing resource suffixes."
        exit 1
    }
    if [[ -n "$EXISTING_SUFFIXES" ]]; then
        if [[ -n "$RESOURCE_SUFFIX" ]]; then
            if ! grep -Fxq -- "$RESOURCE_SUFFIX" <<< "$EXISTING_SUFFIXES"; then
                log_error "Selected suffix $RESOURCE_SUFFIX does not match an existing environment."
                printf 'Existing suffixes:\n%s\n' "$EXISTING_SUFFIXES"
                exit 1
            fi
        elif [[ "$EXISTING_SUFFIXES" == *$'\n'* ]]; then
            log_error "Multiple environment suffixes found; no resources were changed."
            printf 'Existing suffixes:\n%s\n' "$EXISTING_SUFFIXES"
            echo "Inspect the environments, then rerun with --resource-suffix <suffix-to-keep>."
            echo "This selects the environment to update; it does not delete the others."
            exit 1
        else
            RESOURCE_SUFFIX="$EXISTING_SUFFIXES"
        fi
    fi
    log_warning "Resource group $RESOURCE_GROUP already exists"
    read -p "Do you want to continue with deployment? (y/N) " -n 1 -r
    echo
    if [[ ! $REPLY =~ ^[Yy]$ ]]; then
        echo "Aborted."
        exit 0
    fi
else
    # Create resource group
    log_info "Creating resource group: $RESOURCE_GROUP in $LOCATION"
    
    # Build tags string: base tags + any additional tags provided
    ALL_TAGS="hackuser=$USERNAME environment=hack createdBy=automation"
    if [ -n "$ADDITIONAL_TAGS" ]; then
        ALL_TAGS="$ALL_TAGS $ADDITIONAL_TAGS"
    fi
    
    az group create \
        --name "$RESOURCE_GROUP" \
        --subscription "$SUBSCRIPTION_ID" \
        --location "$LOCATION" \
        --tags $ALL_TAGS \
        --output none
    log_success "Resource group created"
fi

DEPLOYMENT_PARAMETERS=("location=$LOCATION")
if [[ -n "$RESOURCE_SUFFIX" ]]; then
    log_info "Reusing resource suffix: $RESOURCE_SUFFIX"
    DEPLOYMENT_PARAMETERS+=("resourceSuffix=$RESOURCE_SUFFIX")
else
    log_info "Using stable resource names derived from the resource-group ID."
fi

# Define scope for role assignments
RG_SCOPE="/subscriptions/$SUBSCRIPTION_ID/resourceGroups/$RESOURCE_GROUP"

# Assign permissions
log_info "Assigning roles to $USERNAME on resource group $RESOURCE_GROUP"

assign_role() {
    local role_name="$1"
    if az role assignment create \
        --assignee "$USER_ID" \
        --role "$role_name" \
        --scope "$RG_SCOPE" \
        --subscription "$SUBSCRIPTION_ID" \
        --output none 2>&1; then
        log_success "$role_name role assigned"
    else
        log_warning "Could not assign $role_name (may already exist)"
    fi
}

assign_role "Owner"
assign_role "Azure AI Developer"
assign_role "Cognitive Services User"
assign_role "Search Service Contributor"

log_success "All roles assigned for $USERNAME"

# Deploy infrastructure
log_info "Deploying infrastructure (this will take 5-15 minutes)..."

DEPLOYMENT_NAME="hack-deployment-$(date +%s)"
START_TIME=$(date +%s)

if az deployment group create \
    --name "$DEPLOYMENT_NAME" \
    --resource-group "$RESOURCE_GROUP" \
    --subscription "$SUBSCRIPTION_ID" \
    --template-file "$TEMPLATE_FILE" \
    --parameters "${DEPLOYMENT_PARAMETERS[@]}" \
    --output json > "deployment-$RESOURCE_GROUP.json"; then
    
    END_TIME=$(date +%s)
    DURATION=$((END_TIME - START_TIME))
    MINUTES=$((DURATION / 60))
    SECONDS=$((DURATION % 60))
    
    log_success "Deployment successful for $RESOURCE_GROUP (${MINUTES}m ${SECONDS}s)"
    
    # Show key outputs
    echo ""
    echo "=========================================="
    echo "Deployment Outputs"
    echo "=========================================="
    jq -r '.properties.outputs | to_entries[] | select(.value.type != "securestring") | "\(.key): \(.value.value)"' "deployment-$RESOURCE_GROUP.json" 2>/dev/null || \
        az deployment group show --name "$DEPLOYMENT_NAME" --resource-group "$RESOURCE_GROUP" --query "properties.outputs" -o table
    echo "=========================================="
    echo ""
    log_success "Provisioning complete for user: $USERNAME"
    echo "Deployment details saved to: deployment-$RESOURCE_GROUP.json"
else
    log_error "Deployment failed for $RESOURCE_GROUP"
    echo "Failed deployment: $DEPLOYMENT_NAME"
    if ! az deployment operation group list \
        --name "$DEPLOYMENT_NAME" \
        --resource-group "$RESOURCE_GROUP" \
        --subscription "$SUBSCRIPTION_ID" \
        --query "[?properties.provisioningState=='Failed'].{resource:properties.targetResource.resourceName,type:properties.targetResource.resourceType,error:properties.statusMessage}" \
        --output json; then
        log_warning "Could not retrieve failed operations. Check this deployment in the Azure portal."
    fi
    exit 1
fi
