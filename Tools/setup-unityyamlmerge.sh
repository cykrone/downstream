#!/usr/bin/env bash
# Registers Unity's Smart Merge as the git merge driver used by .gitattributes.
# Usage: Tools/setup-unityyamlmerge.sh "/path/to/UnityYAMLMerge"
#   Windows: "C:/Program Files/Unity/Hub/Editor/6000.3.26f1/Editor/Data/Tools/UnityYAMLMerge.exe"
#   macOS:   "/Applications/Unity/Hub/Editor/6000.3.26f1/Unity.app/Contents/Helpers/UnityYAMLMerge"
set -euo pipefail
tool="${1:?Pass the path to UnityYAMLMerge}"
git config merge.unityyamlmerge.name "Unity SmartMerge (UnityYAMLMerge)"
git config merge.unityyamlmerge.driver "\"$tool\" merge -h -p --force %O %B %A %A"
git config merge.unityyamlmerge.recursive binary
echo "Smart Merge registered for this clone."
