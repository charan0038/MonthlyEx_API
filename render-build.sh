#!/usr/bin/env bash
set -e

echo "===> Checking for dotnet SDK..."
if ! command -v dotnet &> /dev/null; then
    echo "===> .NET not found in environment. Installing .NET 9.0 SDK to local folder..."
    export DOTNET_INSTALL_DIR="$PWD/.dotnet"
    mkdir -p "$DOTNET_INSTALL_DIR"
    curl -sSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --channel 9.0 --install-dir "$DOTNET_INSTALL_DIR"
    export PATH="$DOTNET_INSTALL_DIR:$PATH"
    export DOTNET_ROOT="$DOTNET_INSTALL_DIR"
fi

echo "===> Using .NET SDK Version: $(dotnet --version)"
echo "===> Publishing MonthExpenses.Api..."
dotnet publish MonthExpenses.Api.csproj -c Release -o out

echo "===> Publish complete successfully!"
