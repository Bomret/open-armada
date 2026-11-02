# Knowledge base: GitHub wiki

Durable, human-facing docs (file-format specifications, design overviews) published to the
wiki of `Bomret/open-armada` on GitHub. In-repo docs (`docs/agents/documentation.md`) remain the source of
truth; the wiki is a published view.

## Configuration

- **Backend:** GitHub repository wiki of `Bomret/open-armada`, reached as its git repository
  `git@github.com:Bomret/open-armada.wiki.git`. GitHub has no REST API for wiki pages, so every call is a
  git clone, and every write a commit and push to that repository.
- **The wiki git repository exists only after its first page.** GitHub creates it when a page is saved in
  the web UI (`https://github.com/Bomret/open-armada/wiki`). Until then every clone answers "Repository not
  found", and each template below answers exit 2, "could not answer". Create the `Home` page in the web UI
  once, before the first `/superdev:publish-kb`.
- **Auth:** the SSH key `git` uses for `github.com`. No token environment variable is read. A clone or push
  that fails on auth stops the step: each template below answers exit 2 (`find`) or exit 1 (the others),
  and `/superdev:publish-kb` leaves the page staged.
- **Pages are files.** A page titled `<title>` is the file `<title with each space as ->.md` at the root
  of the wiki repository, and GitHub shows it at `https://github.com/Bomret/open-armada/wiki/<that name>`.
  A title holding `/`, `-`, or a character outside letters, digits, spaces, `.`, and `_` is refused with
  exit 2, because its file name cannot be mapped back to one title.
  changelog: durable, reader-facing docs only, not every `AGENTS.md`.
- **Hierarchy:** links. A GitHub wiki nests no pages. Each child page links from a list item in its
  parent's body, and `/superdev:publish-kb` writes that link (ADR 0168).

## Audience, language & outline

The wiki is **human-consumption first**. Recorded at setup:

- **Audience(s):** players, modders, contributors/developers.
- **Language:** English. All wiki prose is written in this language, per the declarations in
  `docs/agents/documentation.md`.

## Outline

A living skeleton. `/superdev:plan` and `/superdev:document` fill and extend it.

- **`Home`.** The root page, with no parent. Hand-written. Each group section below holds one link list
  that `/superdev:publish-kb` maintains.
- **Players.** `Getting started`, `Map sandbox`, `Known limitations`. Parent: `Home`, section `Players`.
- **Modders.** `File formats`, `Asset loading`, `Known but not loaded files`, `Built-in maps`,
  `Combat rules`. Parent: `Home`, section `Modders`.
- **File formats.** One page per format: `SOD models`, `ODF definitions`, `BZN maps`, `SPR sprites`,
  `Textures`. Parent: `File formats`, section `Formats`.
- **Developers.** `Architecture`, `Build and setup`, `Roadmap`, `Selection display`, `Play runs`.
  Parent: `Home`, section `Developers`.
- **`Changelog`.** `(aggregate)`: one `## <feature>` section per feature, contributed by the scope that
  built it. Parent: `Home`, section `Developers`.

**Create the root page first.** A child published before its root lands, but stays staged as not linked,
because its parent has no page to carry the link yet.

**`(aggregate)` marks a page many scopes contribute to.** Such a page is staged one section at a time
(`action: patch`) and merged into the live page by that section's heading. Every other page is staged
whole (ADR 0027).

**Links.** A parent's link line has the form `- [<title>](https://github.com/Bomret/open-armada/wiki/{sub_url}): <one line>`.
`<title>` is the child's full title. `{sub_url}` is written literally. The doc-writer writes the whole line,
placeholder included, and `/superdev:publish-kb` replaces `{sub_url}` with the value the find call returns
for the child once the child has landed: the page's file name without `.md`. `/superdev:publish-kb` then
places the line after the last top-level list item of the named section's own content, or below the
heading as its first list item when that content holds none yet.

## Staging

This is a **remote** backend, so nothing but `/superdev:publish-kb` calls it (ADR 0023). `/superdev:plan`
and `/superdev:document` write each page as a **staged page** under `.superdev/kb-pending/<scope>/` in the
main working tree, resolved by `scope.js staging <scope>` (ADR 0119). Each carries YAML frontmatter with
the page `title` and an `action` of `upsert`, `patch`, or `delete`, and the page body below. A page reaches
the wiki when a human runs `/superdev:publish-kb`, after the feature has reached the readers this wiki is
written for.

## Wiki git conventions

- **Every command works in its own fresh clone**, `git clone -q --depth 1` into a `mktemp -d` directory
  that the command removes on exit. Nothing is cached between calls, so a page another writer pushed is
  always seen.
- **A write is one commit, pushed to the wiki's default branch** (`git push -q origin HEAD`), with the
  message `superdev: publish <title>`. A rejected push (another writer pushed in between) answers exit 1,
  and the page stays staged for the next run.
- **Find** maps the title to its file name and tests for that file in the clone. A present file answers
  `{"sub_url": "<name>", "file": "<name>.md"}`; an absent one answers exit 3. A failed clone answers
  exit 2: a missing wiki repository and a failed login look the same to `git`.
- **Read** prints the file's content, which is the page markdown.

## Commands

The **backend command contract** (ADR 0069) `scripts/publish-kb.js` runs. Each command runs as
`sh -c <template> sh <path>`: `$1` names a JSON file holding `title`, `bodyFile` (`create` and `update`),
the `find` result's fields under `found` (`read`, `update`, and `delete`), and the `knowledge-base`
section of `.superdev/info.json` under `operator` (ADR 0207). Every command reads its values from that
file with `jq`. `find` emits the page's `sub_url` and `file`; `read`, `update`, and `delete` take
`.found.file` from it, and `/superdev:publish-kb` fills a parent link's `{sub_url}` from it (ADR 0168).
`find` answers exit 3, the only outcome `scripts/publish-kb.js` reads as positive absence, only after a
successful clone that lacks the file; any other failure answers exit 2, "could not answer". `create`,
`update`, and `delete` map any failed step to exit 1.

Every command below runs under a **30-second bound** (ADR 0130), or whatever
`knowledge-base.timeoutMs` of `.superdev/info.json` names. `scripts/publish-kb.js` applies it at the spawn. A command killed at
the bound leaves its page staged and **stops the run**: the remaining pages are reported untried and stay
staged for the next one.

### find

```sh
name=$(jq -er '.title | if test("^[A-Za-z0-9._ ]+$") then gsub(" "; "-") else error("unmappable title") end' "$1" 2>/dev/null) || { echo "superdev: the title cannot be mapped to a GitHub wiki file name" >&2; exit 2; }
d=$(mktemp -d) || exit 2
trap 'rm -rf "$d"' EXIT
git clone -q --depth 1 git@github.com:Bomret/open-armada.wiki.git "$d" 2>/dev/null || { echo "superdev: cannot clone git@github.com:Bomret/open-armada.wiki.git; check the SSH login for github.com, and create the Home page in the web UI if the wiki has no page yet" >&2; exit 2; }
[ -f "$d/$name.md" ] || exit 3
jq -cn --arg n "$name" '{sub_url: $n, file: ($n + ".md")}'
```

### read

```sh
file=$(jq -er '.found.file' "$1") || exit 1
d=$(mktemp -d) || exit 1
trap 'rm -rf "$d"' EXIT
git clone -q --depth 1 git@github.com:Bomret/open-armada.wiki.git "$d" 2>/dev/null || exit 1
cat "$d/$file"
```

### create

```sh
name=$(jq -er '.title | if test("^[A-Za-z0-9._ ]+$") then gsub(" "; "-") else error("unmappable title") end' "$1") || exit 1
title=$(jq -er '.title' "$1") || exit 1
body=$(jq -er '.bodyFile' "$1") || exit 1
d=$(mktemp -d) || exit 1
trap 'rm -rf "$d"' EXIT
git clone -q --depth 1 git@github.com:Bomret/open-armada.wiki.git "$d" || exit 1
[ -e "$d/$name.md" ] && { echo "superdev: $name.md already exists" >&2; exit 1; }
cp "$body" "$d/$name.md" || exit 1
git -C "$d" add -- "$name.md" && git -C "$d" commit -q -m "superdev: publish $title" && git -C "$d" push -q origin HEAD || exit 1
```

### update

```sh
file=$(jq -er '.found.file' "$1") || exit 1
title=$(jq -er '.title' "$1") || exit 1
body=$(jq -er '.bodyFile' "$1") || exit 1
d=$(mktemp -d) || exit 1
trap 'rm -rf "$d"' EXIT
git clone -q --depth 1 git@github.com:Bomret/open-armada.wiki.git "$d" || exit 1
[ -f "$d/$file" ] || exit 1
cp "$body" "$d/$file" || exit 1
git -C "$d" add -- "$file" || exit 1
git -C "$d" diff --cached --quiet && exit 0
git -C "$d" commit -q -m "superdev: publish $title" && git -C "$d" push -q origin HEAD || exit 1
```

### delete

```sh
file=$(jq -er '.found.file' "$1") || exit 1
title=$(jq -er '.title' "$1") || exit 1
d=$(mktemp -d) || exit 1
trap 'rm -rf "$d"' EXIT
git clone -q --depth 1 git@github.com:Bomret/open-armada.wiki.git "$d" || exit 1
git -C "$d" rm -q -- "$file" && git -C "$d" commit -q -m "superdev: delete $title" && git -C "$d" push -q origin HEAD || exit 1
```

## superdev usage

- `/superdev:plan` and `/superdev:document` stage pages; `/superdev:document` reconciles and extends the
  wiki through its doc-writer.
- `/superdev:publish-kb` sends the staged pages to the wiki. The only command that calls it.

## Degrade / switching

No `git` or `jq`, a failed clone or push, or a wiki with no page yet → `/superdev:publish-kb` leaves every
page staged, reports why, and deletes nothing; a later run retries exactly the pages that did not land. The
in-repo docs are the record meanwhile. To move to a different knowledge base later, re-run
`/superdev:setup` and pick a different one.
