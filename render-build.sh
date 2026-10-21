#!/usr/bin/env bash
set -e

echo "===> Checking for dotnet..."
export DOTNET_INSTALL_DIR="$PWD/.dotnet"

if ! command -v dotnet &> /dev/null; then
    echo "===> .NET SDK not found. Downloading Microsoft .NET 9.0 SDK installer..."
    mkdir -p "$DOTNET_INSTALL_DIR"
    curl -sSL https://dot.net/v1/dotnet-install.sh -o dotnet-install.sh
    chmod +x dotnet-install.sh
    ./dotnet-install.sh --channel 9.0 --install-dir "$DOTNET_INSTALL_DIR"
    rm -f dotnet-install.sh
fi

export PATH="$DOTNET_INSTALL_DIR:$PATH"
export DOTNET_ROOT="$DOTNET_INSTALL_DIR"

echo "===> .NET SDK version:"
dotnet --version

echo "===> Restoring and publishing MonthExpenses.Api..."
dotnet publish MonthExpenses.Api.csproj -c Release -o out

echo "===> Build and publish completed successfully!"
