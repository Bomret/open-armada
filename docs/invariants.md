# Invariant register

### INV-0001

Class: legal
Concepts: Install, Asset
Statement: The repository contains no bytes taken from an install; tests build their fixtures in code.
Breaks: silently
Check: none — no cheap test can tell a copied byte sequence from a synthesized one; reviewers check that fixtures are built in code.

### INV-0002

Class: behavior
Concepts: Asset, Game file system, Install
Statement: An asset that is missing or fails to parse is reported with its name and reason and skipped; it never stops the engine.
Breaks: loudly

### INV-0003

Class: behavior
Concepts: Install, Object definition, Game constants, Stand-in
Statement: A tunable that the install defines is read from the install at run time; a value the engine supplies itself is a stand-in, named as one in a comment where it is used.
Breaks: silently
Check: none — no cheap test tells a hard-coded copy of an install value from a deliberate stand-in; reviewers check that every engine-supplied tunable carries a `stand-in:` comment.
