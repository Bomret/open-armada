#!/bin/sh
# The focused test command: runs the unit tests a set of changed paths needs, from the repository root.
# It skips the integration tests, the categories of tests/OpenArmada.Tests/Needs.cs.
# Changed test files run their own test classes: the type after each [TestClass] line, past any attribute lines
# between them. Any other changed path, except Markdown, runs every test,
# since nothing maps a source file to the tests that cover it. Markdown alone runs nothing.
set -eu

unit='TestCategory!=Install&TestCategory!=Godot'

filter=""
for path in "$@"; do
  case "$path" in
    *.md) continue ;;
    tests/OpenArmada.Tests/*.cs)
      classes=$(awk '
        /^[ \t]*\[TestClass\]/ { open = 1; next }
        open && /^[ \t]*(\[|\/\/)/ { next }
        open {
          if (match($0, /(^|[ \t])class[ \t]+[A-Za-z0-9_]+/)) {
            name = substr($0, RSTART, RLENGTH)
            sub(/.*class[ \t]+/, "", name)
            print name
          }
          open = 0
        }' "$path" 2>/dev/null || true)
      if [ -n "$classes" ]; then
        for class in $classes; do
          filter="${filter:+$filter|}FullyQualifiedName~OpenArmada.Tests.$class."
        done
        continue
      fi
      ;;
  esac
  exec dotnet test --project tests/OpenArmada.Tests --filter "$unit"
done

if [ -z "$filter" ]; then
  echo "focused-tests.sh: no changed path needs a test"
  exit 0
fi
# Exit 8, no test matched, is a pass: a changed file may hold integration tests only.
exec dotnet test --project tests/OpenArmada.Tests --filter "($filter)&$unit" --ignore-exit-code 8
