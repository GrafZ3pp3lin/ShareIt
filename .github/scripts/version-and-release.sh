#!/usr/bin/env bash
# Computes the next SemVer from Conventional Commits since the last `v*` tag
# (or uses VERSION_OVERRIDE when set), writes categorized release notes, creates
# the git tag and the GitHub Release.
#
# VERSION_OVERRIDE (optional): exact stable SemVer to release instead of the
# commit-derived bump, e.g. "1.0.0". It must be greater than the latest tag.
#
# Expects a checkout with full history and tags (fetch-depth: 0, fetch-tags).
# Prints the bare version (e.g. "1.2.0") to stdout; the workflow captures it
# into its job output.
set -euo pipefail

# --- Next version --------------------------------------------------------------

latest_tag="$(git tag --list 'v*' --sort=-v:refname | head -n 1 || true)"
range="${latest_tag:+$latest_tag..HEAD}"
range="${range:-HEAD}"

# Never release the same commit twice: if the last release already points at
# HEAD there is nothing new to tag, even if someone re-runs the workflow.
if [[ -n "$latest_tag" && "$(git rev-parse "$latest_tag^{commit}")" == "$(git rev-parse HEAD)" ]]; then
  echo "::error::HEAD is already released as $latest_tag; refusing to create another release for the same commit" >&2
  exit 1
fi

# Optional manual version from the workflow input, e.g. to publish 1.0.0 when
# the commit-derived bump would only be a minor release.
requested_version="${VERSION_OVERRIDE:-}"
manual_version=""
if [[ -n "$requested_version" ]]; then
  manual_version="${requested_version#v}"
  if [[ ! "$manual_version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
    echo "::error::Requested version must be a stable SemVer like 1.2.3 (got: $requested_version)" >&2
    exit 1
  fi
  if [[ -n "$latest_tag" ]]; then
    IFS='.' read -r latest_maj latest_min latest_pat <<< "${latest_tag#v}"
    IFS='.' read -r new_maj new_min new_pat <<< "$manual_version"
    if (( new_maj < latest_maj ||
          (new_maj == latest_maj && new_min < latest_min) ||
          (new_maj == latest_maj && new_min == latest_min && new_pat <= latest_pat) )); then
      echo "::error::Requested version $manual_version must be greater than the latest tag $latest_tag" >&2
      exit 1
    fi
  fi
fi

# Version bumps:
#   feat                    -> minor
#   fix, perf, build, chore -> patch
#   any type with !         -> major
#   BREAKING CHANGE         -> major
#   everything else         -> no bump (release notes only)
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
    build|chore) others+=("- $subject"); if (( breaking )); then major=1; else patch=1; fi ;;
    *)    others+=("- $subject"); (( breaking )) && major=1 || true ;;
  esac
  (( breaking )) && breakings+=("- $subject") || true
done < <(git log --format=$'%s%x1f%b%x1e' "$range")

if [[ -n "$manual_version" ]]; then
  version="$manual_version"
elif [[ -z "$latest_tag" ]]; then
  # First release: no baseline to bump from.
  version="0.1.0"
else
  if (( !major && !minor && !patch )); then
    echo "::error::No releasable commits (feat/fix/perf/build/chore/BREAKING CHANGE) since $latest_tag" >&2
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
# `gh` prints the release URL to stdout; send it to stderr so stdout stays
# reserved for the bare version.
gh release create "v$version" \
  --target "$(git rev-parse HEAD)" \
  --title "v$version" \
  --notes-file "$notes_file" >&2

echo "$version"
