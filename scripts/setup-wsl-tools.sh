#!/bin/bash
#
# Installs the tool chain needed to run the Agentic Factory demo inside a fresh Ubuntu 24.04 WSL distro.
# Run as root:  wsl -d Ubuntu-24.04 -u root -- bash /mnt/c/github/agentic-factory-hack/scripts/setup-wsl-tools.sh
#
set -euo pipefail

DEMO_USER="${DEMO_USER:-alon}"
export DEBIAN_FRONTEND=noninteractive

echo "==> Creating user $DEMO_USER (passwordless sudo, default WSL user)"
if ! id "$DEMO_USER" &>/dev/null; then
    useradd -m -s /bin/bash -G sudo "$DEMO_USER"
    echo "$DEMO_USER ALL=(ALL) NOPASSWD:ALL" > "/etc/sudoers.d/$DEMO_USER"
    chmod 440 "/etc/sudoers.d/$DEMO_USER"
fi
printf "[user]\ndefault=%s\n" "$DEMO_USER" > /etc/wsl.conf

echo "==> Base packages"
apt-get update -y
apt-get install -y curl ca-certificates unzip jq git dos2unix python3 python3-pip python3-venv python3-dev build-essential libicu74 gnupg lsb-release apt-transport-https

echo "==> Node.js LTS"
curl -fsSL https://deb.nodesource.com/setup_lts.x | bash -
apt-get install -y nodejs

echo "==> .NET SDK 10"
curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
bash /tmp/dotnet-install.sh --channel 10.0 --install-dir /usr/share/dotnet
ln -sf /usr/share/dotnet/dotnet /usr/local/bin/dotnet

echo "==> Azure CLI"
curl -sL https://aka.ms/InstallAzureCLIDeb | bash

echo "==> uv"
curl -LsSf https://astral.sh/uv/install.sh | env UV_INSTALL_DIR=/usr/local/bin UV_NO_MODIFY_PATH=1 sh

echo "==> Aspire CLI (installed for $DEMO_USER)"
sudo -u "$DEMO_USER" -H bash -c 'curl -fsSL https://aspire.dev/install.sh | bash -s'

echo "==> Versions"
python3 --version
node --version
dotnet --version
az version --query '"azure-cli"' -o tsv || true
uv --version
sudo -u "$DEMO_USER" -H bash -lc 'export PATH="$HOME/.aspire/bin:$PATH"; aspire --version' || true
echo "==> Done. Restart the distro once so /etc/wsl.conf takes effect:  wsl --terminate Ubuntu-24.04"
