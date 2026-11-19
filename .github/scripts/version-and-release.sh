#!/usr/bin/env bash
# Computes the next SemVer from Conventional Commits since the last `v*` tag,
# writes categorized release notes, creates the git tag and the GitHub Release.
#
# Expects a checkout with full history and tags (fetch-depth: 0, fetch-tags).
# Prints the bare version (e.g. "1.2.0") to stdout; the workflow captures it
# into its job output.
set -euo pipefail

# --- Next version --------------------------------------------------------------

latest_tag="$(git tag --list 'v*' --sort=-v:refname | head -n 1 || true)"
range="${latest_tag:+$latest_tag..HEAD}"
range="${range:-HEAD}"

# Categorize conventional commits: feat -> minor, fix/perf -> patch,
# feat! / BREAKING CHANGE -> major.
declare -a feats=() fixes=() perfs=() breakings=() others=()
major=0 minor=0 patch=0
while IFS=$'\x1f' read -r -d $'\x1e' subject body; do
  [[ -z "$subject" ]] && continue
  subject="${subject#$'\n'}" # git log terminates each record with \n

  type="${subject%%:*}" # before the first colon
  type="${type%%!*}"    # strip breaking marker
  type="${type%%(*}"    # strip scope

  breaking=0
  [[ "${subject%%:*}" == *'!'* ]] && breaking=1
  [[ "$body" == *"BREAKING CHANGE:"* || "$body" == *"BREAKING-CHANGE:"* ]] && breaking=1

  case "$type" in
    feat) feats+=("- $subject"); if (( breaking )); then major=1; else minor=1; fi ;;
    fix)  fixes+=("- $subject"); if (( breaking )); then major=1; else patch=1; fi ;;
    perf) perfs+=("- $subject"); if (( breaking )); then major=1; else patch=1; fi ;;
    *)    others+=("- $subject"); (( breaking )) && major=1 || true ;;
  esac
  (( breaking )) && breakings+=("- $subject") || true
done < <(git log --format=$'%s%x1f%b%x1e' "$range")

if [[ -z "$latest_tag" ]]; then
  # First release: no baseline to bump from.
  version="0.1.0"
else
  if (( !major && !minor && !patch )); then
    echo "::error::No releasable commits (feat/fix/perf/BREAKING CHANGE) since $latest_tag" >&2
    exit 1
  fi

  IFS='.' read -r maj min pat <<< "${latest_tag#v}"
  if (( major )); then
    maj=$((maj + 1)); min=0; pat=0
  elif (( minor )); then
    min=$((min + 1)); pat=0
  else
    pat=$((pat + 1))
  fi
  version="$maj.$min.$pat"
fi

# --- Release notes -------------------------------------------------------------

notes_file="$(mktemp)"
{
  echo "## What's Changed"
  echo
  if (( ${#breakings[@]} )); then
    echo '### ⚠️ Breaking Changes'
    echo
    printf '%s\n' "${breakings[@]}"
    echo
  fi
  if (( ${#feats[@]} )); then
    echo '### ✨ Features'
    echo
    printf '%s\n' "${feats[@]}"
    echo
  fi
  if (( ${#fixes[@]} )); then
    echo '### 🐛 Bug Fixes'
    echo
    printf '%s\n' "${fixes[@]}"
    echo
  fi
  if (( ${#perfs[@]} )); then
    echo '### ⚡ Performance'
    echo
    printf '%s\n' "${perfs[@]}"
    echo
  fi
  if (( ${#others[@]} )); then
    echo '### 🧹 Other Changes'
    echo
    printf '%s\n' "${others[@]}"
    echo
  fi
} > "$notes_file"

# --- Tag + GitHub Release ------------------------------------------------------

# Creates the tag at the current commit and publishes the release in one step.
gh release create "v$version" \
  --target "$(git rev-parse HEAD)" \
  --title "v$version" \
  --notes-file "$notes_file"

echo "$version"
