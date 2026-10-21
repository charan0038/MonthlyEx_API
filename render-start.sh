#!/usr/bin/env bash
set -e

if [ -d "$PWD/.dotnet" ]; then
    export PATH="$PWD/.dotnet:$PATH"
    export DOTNET_ROOT="$PWD/.dotnet"
fi

export ASPNETCORE_HTTP_PORTS="${PORT:-10000}"
export ASPNETCORE_URLS="http://0.0.0.0:${PORT:-10000}"

echo "===> Starting MonthExpenses.Api on port ${PORT:-10000}..."
exec dotnet ./out/MonthExpenses.Api.dll
