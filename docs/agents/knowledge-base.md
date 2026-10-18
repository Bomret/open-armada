# Knowledge base: Codeberg wiki

An external knowledge base for durable, human-facing docs (file-format specifications, design overviews,
ADR summaries), published to the wiki of `bomret/open-armada` on Codeberg. In-repo docs
(`docs/agents/documentation.md`) remain the source of truth; the wiki is a published view.

## Audience, language & outline

The KB is **human-consumption first**.

- **Audience(s):** players, modders, contributors/developers.
- **Language:** English — all KB prose is written in this language, per the declarations in
  `docs/agents/documentation.md`.
- **Outline** (living skeleton; `/superdev:plan` and `/superdev:document` fill and extend it):

  - `Players` → `Getting started`, `Map sandbox`, `Known limitations`
  - `Modders` → `File formats` (one page per format: `SOD models`, `ODF definitions`, `BZN maps`,
    `SPR sprites`, `Textures`), `Asset loading`, `Known but not loaded files`, `Built-in maps`,
    `Combat rules`
  - `Developers` → `Architecture`, `Build and setup`, `Roadmap`, `Selection display`, `Play runs`, `ADR summaries` (aggregate,
    sections `## ADR NNNN: <title>`), `Changelog` (aggregate, sections `## <feature>`)

**`(aggregate)` marks a page many scopes contribute to.** `/superdev:document` stages one **section** for
such a page (`action: patch`) rather than the whole page, and `/superdev:publish-kb` merges that section
into the live page by its heading. The heading level is pinned in the outline above: every section of
`ADR summaries` and `Changelog` is a `##` heading, never deeper and never `#`. An unmarked page is
replaced wholesale on publish.

**The wiki is flat.** Forgejo wikis have no page nesting, so the outline's hierarchy exists only as links:
`Home` links each audience page, and each parent page links its children. A page no other page links
cannot be reached from `Home`. A staged page's `parent:` names its outline parent. When `create` makes a
new page, it records the parent in the wiki commit message and adds a link to the new page at the end of
the parent page. An update adds no link: to link an existing page, stage an `upsert` of its parent.
Do not stage an `upsert` of a parent in the same publish as a new child of it: the child's `create`
changes the parent first, so the parent's upsert is refused and stays staged. `create` adds the child's
link itself; stage a parent upsert made for other reasons in a later publish.

**Page names.** A link in a page body names the target page by its wiki name, which Forgejo derives from
the title. Spaces become `-`: `Play runs` is `Play-runs`. A title that holds a `-` keeps its spaces, gets
a `.-` suffix, and is query-escaped, so a space becomes `+`: `Built-in maps` is `Built-in+maps.-`. The
`sub_url` of each entry in the wiki's page list gives the name of an existing page.

## Configuration

- **Backend:** Codeberg (Forgejo) wiki, through the Forgejo wiki API.
- **Host / owner / repo:** `https://codeberg.org` · `bomret` / `open-armada`.
- **Login:** resolved from the Host, never stored: the one `tea login list -o json` entry whose `url`
  equals the Host. No token env var, header, or raw HTTP call (curl) appears in this document.
- **What to publish:** file-format specifications, design overviews, player guides, ADR summaries, and
  the changelog — durable, reader-facing docs only, not every `AGENTS.md`.

## Staging

The wiki is a **remote** backend, so nothing but `/superdev:publish-kb` calls it (ADR 0023).
`/superdev:plan` and `/superdev:document` write each page as a **staged page** under
`.superdev/kb-pending/<scope>/` in the main working tree, resolved by `scope.js staging <scope>`
(ADR 0119) — YAML frontmatter carrying the page `title` and an `action` of `upsert`, `patch`, or
`delete`, the page body below. A page the outline places under another page also carries
`parent: <that page's title>`. A page reaches the wiki when a human runs `/superdev:publish-kb`, after the
feature has reached the readers this wiki is written for.

## Commands

The **backend command contract** (ADR 0069) `scripts/publish-kb.js` runs. Every call goes through
`tea api` with `--login codeberg.org --repo bomret/open-armada`; `tea api` fills `{owner}` and `{repo}`
in the path from `--repo`. It exits 0 on a 4xx or 5xx answer, so each command passes `-i` and reads the
HTTP status line from stderr.

- A page's identity is its wiki name. `find` pages through the wiki's page list (`/wiki/pages`, up to
  50 entries a page) until a listing page returns no entries, since Forgejo can return a short page
  before the last one. It takes the entry whose title equals the staged title exactly, and returns
  that entry's `sub_url` as `id`. The `id` reaches `read`, `update`, and `delete` as `SUPERDEV_KB_ID` and resolves a
  staged page's `parent:` as `SUPERDEV_KB_PARENT_ID`.
- `find` exits 3 when no listed page carries the title, at the first empty listing page: the only
  outcome `scripts/publish-kb.js` reads as positive absence. It exits 2 when any listing page answers
  other than a 200 with a list. A wiki that is switched off answers 404, so `find` exits 2 and the page stays staged.
- `read` prints the live page's markdown body on stdout, nothing else.
- `create` and `update` read the staged body from `SUPERDEV_KB_BODY_FILE` and send it base64-encoded.
- `create` with `SUPERDEV_KB_PARENT_ID` set takes the new page's name from the `sub_url` of the 201
  answer. It reads the parent page and, when the parent does not yet hold a `](<name>)` link, appends
  `- [<title>](<name>)` on a line of its own, with `[` and `]` in the title escaped as `\[` and `\]`,
  and writes the parent back with its own title. When that step fails after the page exists, `create`
  exits 1 with a message that names the parent page to link by hand.
- `delete` treats a 404 as success: an absent page is the intended end state.

Every command below runs under a **30-second bound** (ADR 0130), or whatever
`SUPERDEV_PUBLISH_KB_TIMEOUT_MS` names. A command killed at the bound leaves its page staged and stops
the run.

### find

```sh
out=$(mktemp) && err=$(mktemp) || exit 2
p=1
while :; do
  tea api -i --login codeberg.org --repo bomret/open-armada "/repos/{owner}/{repo}/wiki/pages?page=$p&limit=50" >"$out" 2>"$err" || exit 2
  [ "$(head -n1 "$err" | awk '{print $2}')" = 200 ] || exit 2
  n=$(jq -e 'if type == "array" then length else error end' "$out") || exit 2
  id=$(jq -c --arg t "$SUPERDEV_KB_TITLE" 'first(.[] | select(.title == $t) | {id: .sub_url})' "$out") || exit 2
  [ -n "$id" ] && { printf '%s\n' "$id"; exit 0; }
  [ "$n" -eq 0 ] && exit 3
  p=$((p + 1))
done
```

### read

```sh
out=$(mktemp) && err=$(mktemp) || exit 2
tea api -i --login codeberg.org --repo bomret/open-armada "/repos/{owner}/{repo}/wiki/page/$SUPERDEV_KB_ID" >"$out" 2>"$err" || exit 2
[ "$(head -n1 "$err" | awk '{print $2}')" = 200 ] || exit 2
jq -je '.content_base64 | @base64d' "$out"
```

### create

```sh
req=$(mktemp) && out=$(mktemp) && err=$(mktemp) || exit 2
jq -n --arg t "$SUPERDEV_KB_TITLE" --arg p "${SUPERDEV_KB_PARENT_ID:-}" --rawfile c "$SUPERDEV_KB_BODY_FILE" \
  '{title: $t, content_base64: ($c | @base64), message: ("Add " + $t + (if $p == "" then "" else " (under " + $p + ")" end))}' >"$req" || exit 2
tea api -i --login codeberg.org --repo bomret/open-armada -X POST "/repos/{owner}/{repo}/wiki/new" -d @"$req" >"$out" 2>"$err" || exit 2
[ "$(head -n1 "$err" | awk '{print $2}')" = 201 ] || exit 2
[ -n "${SUPERDEV_KB_PARENT_ID:-}" ] || exit 0
unlinked() { echo "created \"$SUPERDEV_KB_TITLE\", but $1: link it from the page \"$SUPERDEV_KB_PARENT_ID\" by hand" >&2; exit 1; }
name=$(jq -re '.sub_url' "$out") || unlinked "the create answer names no page"
tea api -i --login codeberg.org --repo bomret/open-armada "/repos/{owner}/{repo}/wiki/page/$SUPERDEV_KB_PARENT_ID" >"$out" 2>"$err" || unlinked "the parent read failed"
[ "$(head -n1 "$err" | awk '{print $2}')" = 200 ] || unlinked "the parent read failed"
linked=$(jq -r --arg n "$name" '.content_base64 | @base64d | contains("](" + $n + ")")' "$out") || unlinked "the parent read failed"
[ "$linked" = true ] && exit 0
jq --arg t "$SUPERDEV_KB_TITLE" --arg n "$name" '(.content_base64 | @base64d) as $c
  | {title: .title, message: ("Link " + $t + " from " + .title),
     content_base64: ($c + (if $c == "" or ($c | endswith("\n")) then "" else "\n" end) + "- [" + ($t | gsub("\\["; "\\[") | gsub("\\]"; "\\]")) + "](" + $n + ")\n" | @base64)}' \
  "$out" >"$req" || unlinked "the parent write failed"
tea api -i --login codeberg.org --repo bomret/open-armada -X PATCH "/repos/{owner}/{repo}/wiki/page/$SUPERDEV_KB_PARENT_ID" -d @"$req" >/dev/null 2>"$err" || unlinked "the parent write failed"
[ "$(head -n1 "$err" | awk '{print $2}')" = 200 ] || unlinked "the parent write failed"
```

### update

```sh
req=$(mktemp) && err=$(mktemp) || exit 2
jq -n --arg t "$SUPERDEV_KB_TITLE" --rawfile c "$SUPERDEV_KB_BODY_FILE" \
  '{title: $t, content_base64: ($c | @base64), message: ("Update " + $t)}' >"$req" || exit 2
tea api -i --login codeberg.org --repo bomret/open-armada -X PATCH "/repos/{owner}/{repo}/wiki/page/$SUPERDEV_KB_ID" -d @"$req" >/dev/null 2>"$err" || exit 2
[ "$(head -n1 "$err" | awk '{print $2}')" = 200 ]
```

### delete

```sh
err=$(mktemp) || exit 2
tea api -i --login codeberg.org --repo bomret/open-armada -X DELETE "/repos/{owner}/{repo}/wiki/page/$SUPERDEV_KB_ID" >/dev/null 2>"$err" || exit 2
case "$(head -n1 "$err" | awk '{print $2}')" in
  204|404) exit 0 ;;
  *) exit 1 ;;
esac
```

## superdev usage

- `/superdev:plan` — seeds the in-repo feature specification/ADRs into the outline and stages their pages.
- `/superdev:document` — reconciles and extends the KB (via its doc-writer) and stages the pages it
  changed, after a scope lands and on demand.
- `/superdev:publish-kb` — sends the staged pages to the wiki. The only command that calls it.

## Degrade / switching

No `tea` on the path, no `tea` login for `https://codeberg.org`, or an unreachable host →
`/superdev:publish-kb` leaves every page staged, reports why, and deletes nothing; a later run retries
exactly the pages that did not land. The in-repo docs are the record meanwhile. To move to a different
knowledge base later, re-run `/superdev:setup`.
