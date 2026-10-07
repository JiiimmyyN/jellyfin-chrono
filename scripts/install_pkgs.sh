#!/bin/bash

if [ "$CLAUDE_CODE_REMOTE" != "true" ]; then
  exit 0
fi

if command -v dotnet >/dev/null 2>&1 && dotnet --list-sdks 2>/dev/null | grep -q '^10\.'; then
  exit 0
fi

sudo=""
if [ "$(id -u)" != "0" ] && command -v sudo >/dev/null 2>&1; then
  sudo="sudo"
fi

export DEBIAN_FRONTEND=noninteractive
if ! { $sudo apt-get update -qq && $sudo apt-get install -y -qq dotnet-sdk-10.0; } >/tmp/install_pkgs.log 2>&1; then
  curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh \
    && $sudo bash /tmp/dotnet-install.sh --channel 10.0 --install-dir /usr/local/share/dotnet >>/tmp/install_pkgs.log 2>&1 \
    && $sudo ln -sf /usr/local/share/dotnet/dotnet /usr/local/bin/dotnet
fi

if dotnet --list-sdks 2>/dev/null | grep -q '^10\.'; then
  echo ".NET $(dotnet --version) SDK ready."
else
  echo "Warning: the .NET 10 SDK could not be installed (see /tmp/install_pkgs.log). Registry validation and tests need it." >&2
fi

exit 0
