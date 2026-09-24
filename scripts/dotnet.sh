#!/bin/sh
# Homebrew's dotnet needs DOTNET_ROOT; CI and containers already have it.
if [ -z "$DOTNET_ROOT" ] && [ -d /opt/homebrew/opt/dotnet/libexec ]; then
  export DOTNET_ROOT=/opt/homebrew/opt/dotnet/libexec
fi
exec dotnet "$@"
