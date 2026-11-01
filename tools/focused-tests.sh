#!/bin/sh
# The focused test command: runs the unit tests a set of changed paths needs, from the repository root.
# Changed test files run their own test classes. Any other changed path, except Markdown, runs every test,
# since nothing maps a source file to the tests that cover it. Markdown alone runs nothing.
set -eu

filter=""
for path in "$@"; do
  case "$path" in
    *.md) continue ;;
    tests/OpenArmada.Tests/*.cs)
      classes=$(sed -n '/\[TestClass\]/{n;s/.*class \([A-Za-z0-9_]*\).*/\1/p;}' "$path" 2>/dev/null || true)
      if [ -n "$classes" ]; then
        for class in $classes; do
          filter="${filter:+$filter|}FullyQualifiedName~OpenArmada.Tests.$class."
        done
        continue
      fi
      ;;
  esac
  exec dotnet test --project tests/OpenArmada.Tests
done

if [ -z "$filter" ]; then
  echo "focused-tests.sh: no changed path needs a test"
  exit 0
fi
exec dotnet test --project tests/OpenArmada.Tests --filter "$filter"
