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

  - `Players` → `Getting started`, `Known limitations`
  - `Modders` → `File formats` (one page per format: `SOD models`, `ODF definitions`, `BZN maps`,
    `SPR sprites`, `Textures`), `Asset loading`
  - `Developers` → `Architecture`, `Build and setup`, `Roadmap`, `Selection display`, `ADR summaries` (aggregate,
    sections `## ADR NNNN: <title>`), `Changelog` (aggregate, sections `## <feature>`)

**`(aggregate)` marks a page many scopes contribute to.** `/superdev:document` stages one **section** for
such a page (`action: patch`) rather than the whole page, and `/superdev:publish-kb` merges that section
into the live page by its heading. The heading level is pinned in the outline above: every section of
`ADR summaries` and `Changelog` is a `##` heading, never deeper and never `#`. An unmarked page is
replaced wholesale on publish.

**The wiki is flat.** Forgejo wikis have no page nesting, so the outline's hierarchy exists only as a
`Home` page that links each audience's pages. A staged page's `parent:` names its outline parent; the
`create` command records it in the wiki commit message and nests nothing.

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

- A page's identity is its wiki name: the title with spaces replaced by `-`, URL-encoded. `find` returns
  it as `id`, which reaches `read`, `update`, and `delete` as `SUPERDEV_KB_ID` and resolves a staged
  page's `parent:` as `SUPERDEV_KB_PARENT_ID`.
- `find` exits 3 on a 404, the only outcome `scripts/publish-kb.js` reads as positive absence, and 2 on
  any other failure. A wiki that is switched off also answers 404; the following `create` then fails,
  and the page stays staged.
- `read` prints the live page's markdown body on stdout, nothing else.
- `create` and `update` read the staged body from `SUPERDEV_KB_BODY_FILE` and send it base64-encoded.
- `delete` treats a 404 as success: an absent page is the intended end state.

Every command below runs under a **30-second bound** (ADR 0130), or whatever
`SUPERDEV_PUBLISH_KB_TIMEOUT_MS` names. A command killed at the bound leaves its page staged and stops
the run.

### find

```sh
name=$(jq -rn --arg t "$SUPERDEV_KB_TITLE" '$t | gsub(" "; "-") | @uri') || exit 2
out=$(mktemp) && err=$(mktemp) || exit 2
tea api -i --login codeberg.org --repo bomret/open-armada "/repos/{owner}/{repo}/wiki/page/$name" >"$out" 2>"$err" || exit 2
case "$(head -n1 "$err" | awk '{print $2}')" in
  200) jq -ce '{id: .sub_url}' "$out" || exit 2 ;;
  404) exit 3 ;;
  *) exit 2 ;;
esac
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
req=$(mktemp) && err=$(mktemp) || exit 2
jq -n --arg t "$SUPERDEV_KB_TITLE" --arg p "${SUPERDEV_KB_PARENT_ID:-}" --rawfile c "$SUPERDEV_KB_BODY_FILE" \
  '{title: $t, content_base64: ($c | @base64), message: ("Add " + $t + (if $p == "" then "" else " (under " + $p + ")" end))}' >"$req" || exit 2
tea api -i --login codeberg.org --repo bomret/open-armada -X POST "/repos/{owner}/{repo}/wiki/new" -d @"$req" >/dev/null 2>"$err" || exit 2
[ "$(head -n1 "$err" | awk '{print $2}')" = 201 ]
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
