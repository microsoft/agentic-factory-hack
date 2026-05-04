#!/bin/bash
set -e

echo "🔧 Setting up .NET environment for Challenge 2 and 4..."

# Install Aspire
echo "📦 Installing .NET Aspire..."
curl -fsSL https://aspire.dev/install.sh | bash -s

echo "✅ .NET environment setup complete!"
echo "📌 .NET SDK $(dotnet --version) is ready"
echo "📦 All required NuGet packages are cached and ready to use"
