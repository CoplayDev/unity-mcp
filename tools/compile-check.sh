#!/usr/bin/env bash
# Compile MCP for Unity's C# with Roslyn, against Unity's reference assemblies.
#
# WHY THIS EXISTS: Unity will not open a project without an activated license, and GitHub
# withholds secrets from fork PRs -- so PRs to this repo have historically gone unverified
# (unity-tests.yml skips and still reports a green check). This script never launches the
# Editor. It uses a Unity installation purely as a source of reference DLLs and invokes the
# bundled Roslyn compiler directly, which needs no license. That makes a real semantic
# compile check possible on any PR, including forks, with zero secrets.
#
# It does NOT run tests -- that still needs a licensed Editor.
#
# Usage (inside unityci/editor, or against a local Hub install):
#   UNITY_DATA=/opt/unity/Editor/Data UNITY_VERSION=2021.3.45f2 tools/compile-check.sh
#
# Windows, from Git Bash against a Hub install (no Docker, no license):
#   UNITY_DATA="C:/Program Files/Unity/Hub/Editor/2021.3.45f2/Editor/Data" UNITY_VERSION=2021.3.45f2 \
#   EXTRA_REFS=/c/refs tools/compile-check.sh
# where /c/refs holds Newtonsoft.Json.dll and nunit.framework.dll, e.g. copied from
# TestProjects/UnityMCPTests/Library/PackageCache/com.unity.nuget.newtonsoft-json@*/Runtime/ and
# .../com.unity.ext.nunit@*/net35/unity-custom/. Takes ~1 min per Unity version.
#
# Env:
#   UNITY_DATA     Editor/Data directory                 (default /opt/unity/Editor/Data)
#   UNITY_VERSION  e.g. 2021.3.45f2                      (required for version defines)
#   REPO           repo root                             (default: this script's parent)
#   EXTRA_REFS     dir holding Newtonsoft/nunit DLLs     (default $REPO/.compile-refs)
#   PLATFORMS      editor platforms to compile           (default "win osx linux")
#   OUT            scratch dir                           (default /tmp/mcp-compile-check)
#
# MAINTENANCE: tools/compile-refs/{Runtime,Editor}.txt and tools/compile-defines.txt are
# captured from Unity's own generated .csproj files for the pinned defaultVersion. They are
# NOT globs on purpose -- Editor/Data holds the entire .NET 4.8 BCL plus vendored libraries
# (ExCSS.Unity redefines System.Tuple; cscompmgd.dll redefines Microsoft.CSharp.CompilerError)
# that Unity deliberately does not reference. Regenerate them when defaultVersion changes:
# open TestProjects/UnityMCPTests in that Editor, then re-derive from the generated csprojs.
set -uo pipefail

die() { echo "::error::$*" >&2; exit 2; }

# Roslyn runs as a Windows process under Git Bash, so every path handed to it must be
# X:/... form, not the MSYS /x/... form that `pwd` produces there. `pwd -W` is the MSYS
# spelling; on Linux/macOS it is an invalid option and we fall back to plain `pwd`.
winpath() { (cd "$1" 2>/dev/null && (pwd -W 2>/dev/null || pwd)) || die "directory not found: $1"; }

UNITY_DATA=$(winpath "${UNITY_DATA:-/opt/unity/Editor/Data}")
REPO=$(winpath "${REPO:-"$(dirname "${BASH_SOURCE[0]}")/.."}")
EXTRA_REFS=${EXTRA_REFS:-"$REPO/.compile-refs"}
[ -d "$EXTRA_REFS" ] && EXTRA_REFS=$(winpath "$EXTRA_REFS")
PLATFORMS=${PLATFORMS:-"win osx linux"}
OUT=${OUT:-/tmp/mcp-compile-check}
mkdir -p "$OUT" && OUT=$(winpath "$OUT")
LIBCACHE="$UNITY_DATA/Resources/PackageManager/ProjectTemplates/libcache"
CSC="$UNITY_DATA/DotNetSdkRoslyn/csc.dll"
[ -f "$CSC" ] || die "Roslyn compiler not found: $CSC"

DOTNET="$UNITY_DATA/NetCoreRuntime/dotnet"
[ -x "$DOTNET" ] || DOTNET="$(command -v dotnet)" || die "no dotnet runtime available"

UNITY_VERSION=${UNITY_VERSION:-}
[ -n "$UNITY_VERSION" ] || die "UNITY_VERSION must be set (e.g. 2021.3.45f2)"

echo "Unity version : $UNITY_VERSION"
echo "Unity data    : $UNITY_DATA"

# ---------------------------------------------------------------- defines ----
# The version ladder must be exact: defining UNITY_2022_1_OR_NEWER on a 2021.3 build
# compiles the wrong #if branches and invents errors that do not exist.
UNITY_RELEASES="5.3 5.4 5.5 5.6 2017.1 2017.2 2017.3 2017.4 2018.1 2018.2 2018.3 2018.4 \
2019.1 2019.2 2019.3 2019.4 2020.1 2020.2 2020.3 2021.1 2021.2 2021.3 2022.1 2022.2 2022.3 \
6000.0 6000.1 6000.2 6000.3 6000.4 6000.5 6000.6"

ver_major=$(echo "$UNITY_VERSION" | cut -d. -f1)
ver_minor=$(echo "$UNITY_VERSION" | cut -d. -f2)
ver_patch=$(echo "$UNITY_VERSION" | cut -d. -f3 | sed 's/[a-z].*//')

version_defines() {
  local rel rM rm
  for rel in $UNITY_RELEASES; do
    rM=${rel%%.*}; rm=${rel##*.}
    if [ "$rM" -lt "$ver_major" ] || { [ "$rM" -eq "$ver_major" ] && [ "$rm" -le "$ver_minor" ]; }; then
      echo "UNITY_${rM}_${rm}_OR_NEWER"
    fi
  done
  echo "UNITY_${ver_major}"
  echo "UNITY_${ver_major}_${ver_minor}"
  [ -n "$ver_patch" ] && echo "UNITY_${ver_major}_${ver_minor}_${ver_patch}"
}

platform_defines() {
  case "$1" in
    win)   printf '%s\n' UNITY_EDITOR_WIN   UNITY_STANDALONE_WIN   PLATFORM_STANDALONE_WIN ;;
    osx)   printf '%s\n' UNITY_EDITOR_OSX   UNITY_STANDALONE_OSX   PLATFORM_STANDALONE_OSX ;;
    linux) printf '%s\n' UNITY_EDITOR_LINUX UNITY_STANDALONE_LINUX PLATFORM_STANDALONE_LINUX ;;
    *) die "unknown platform '$1' (expected win|osx|linux)" ;;
  esac
}

# --------------------------------------------------------------- references ----
# Resolve one manifest line (DATA/… LIBCACHE/… EXTRA/…) to an absolute path.
resolve_ref() {
  case "$1" in
    DATA/*)     echo "$UNITY_DATA/${1#DATA/}" ;;
    EXTRA/*)    echo "$EXTRA_REFS/${1#EXTRA/}" ;;
    LIBCACHE/*) find "$LIBCACHE" -path '*/ScriptAssemblies/*' -name "${1#LIBCACHE/}" 2>/dev/null | head -1 ;;
  esac
}

# ------------------------------------------------------------------ compile ----
# Output assembly names must be exactly MCPForUnity.Runtime / MCPForUnity.Editor:
# MCPForUnity/Runtime/AssemblyInfo.cs grants InternalsVisibleTo by assembly NAME, so a
# platform suffix in the filename would make Runtime's internals invisible to Editor.
compile() {
  local name="$1" srcdir="$2" platform="$3" manifest="$4"; shift 4
  local dir="$OUT/$platform"; mkdir -p "$dir"
  local rsp="$dir/$name.rsp"
  local missing=0 nrefs=0

  {
    echo "-target:library"
    echo "-langversion:9.0"
    echo "-nostdlib+"
    echo "-preferreduilang:en-US"
    echo "-nowarn:CS1701,CS1702"      # benign netstandard facade version unification
    echo "-out:$dir/$name.dll"
    # ${var%$'\r'} strips the CR a core.autocrlf checkout appends to every line: a CR inside
    # -define:FOO silently defines the wrong symbol, and inside a LIBCACHE/ name it makes
    # `find -name` match nothing, so the Editor build fails on TestRunner/UI types.
    while read -r d; do d=${d%$'\r'}; [ -n "$d" ] && echo "-define:$d"; done < "$REPO/tools/compile-defines.txt"
    version_defines            | while read -r d; do echo "-define:$d"; done
    platform_defines "$platform" | while read -r d; do echo "-define:$d"; done
    while read -r entry; do
      entry=${entry%$'\r'}
      [ -n "$entry" ] || continue
      local p; p=$(resolve_ref "$entry")
      if [ -n "$p" ] && [ -f "$p" ]; then echo "-r:\"$p\""; nrefs=$((nrefs+1))
      else echo "::warning::reference not found: $entry" >&2; missing=$((missing+1)); fi
    done < "$manifest"
    for r in "$@"; do echo "-r:\"$r\""; done
    find "$srcdir" -name '*.cs' -type f | sort | while read -r f; do echo "\"$f\""; done
  } > "$rsp"

  local nsrc; nsrc=$(find "$srcdir" -name '*.cs' -type f | wc -l)
  echo "--- $name [$platform] : $nsrc sources, $(grep -c '^-r:' "$rsp") refs ---"
  "$DOTNET" "$CSC" "@$rsp" 2>&1 | grep -vE '^(Microsoft \(R\)|Copyright)' | sed '/^$/d'
  local rc=${PIPESTATUS[0]}
  if [ "$rc" -ne 0 ] || [ ! -f "$dir/$name.dll" ]; then
    echo "::error::$name failed to compile for $platform"
    return 1
  fi
  echo "OK  $name [$platform]"
}

failed=0
for platform in $PLATFORMS; do
  compile MCPForUnity.Runtime "$REPO/MCPForUnity/Runtime" "$platform" \
    "$REPO/tools/compile-refs/Runtime.txt" || { failed=1; continue; }
  compile MCPForUnity.Editor "$REPO/MCPForUnity/Editor" "$platform" \
    "$REPO/tools/compile-refs/Editor.txt" "$OUT/$platform/MCPForUnity.Runtime.dll" || failed=1
done

[ "$failed" -eq 0 ] || { echo "::error::compile check FAILED"; exit 1; }
echo "compile check passed for: $PLATFORMS"
