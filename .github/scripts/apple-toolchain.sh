#!/usr/bin/env bash
# Pins: dotnet/macios dotnet-10.0.1xx-xcode27.0-10722; runner-images xcode-27.
set -euo pipefail
if [ "$(uname -s)" != Darwin ]; then
  echo "::error::iOS derlemesi macOS gerektirir." >&2
  exit 1
fi
xcode_path=/Applications/Xcode_27.0.app/Contents/Developer
if [ ! -d "$xcode_path" ]; then
  echo "::error::Runner sabit Xcode 27.0 kurulumunu içermiyor; araç eşlemesini gözden geçirin." >&2
  exit 1
fi
sudo xcode-select --switch "$xcode_path"
if [ "$(xcodebuild -version | head -n 1)" != "Xcode 27.0" ]; then
  echo "::error::Beklenen Xcode 27.0 seçilemedi." >&2
  exit 1
fi
if [ "$(dotnet --version)" != "10.0.401" ]; then
  echo "::error::.NET SDK 10.0.401 gereklidir." >&2
  exit 1
fi
dotnet new globaljson --sdk-version 10.0.401 --roll-forward disable --force
dotnet workload install maui-ios --version 10.0.401.1
xcodebuild -version
dotnet workload --info
