# 47. The user documentation follows Diátaxis

Date: 2026-09-30

## Status

Accepted on 2026-09-30, by the maintainer, during M02, each choice below put to them and settled (#148). It
amends point 5 of the definition of done in `docs/roadmap.md` and the milestone template, and every milestone from
M03 on carries it as an exit criterion.

## Context

The user documentation, `docs/website/docs`, had an introduction, the feature comparison and four pages under
*Concepts*. Each of the four did several jobs at once. The page on reader limits explained why the reader has
limits, listed them in a table, and showed how to raise one and how to throw instead. The page on diagnostics set
out the reader's philosophy, held the table of every code, and walked through the search for a stream's end. A
reader looking up a code had to scroll past the reasoning. A reader wanting to raise a limit had to find the one
snippet that did it. Nobody new to the library had a page that taught it to them: the introduction's two snippets
were the nearest thing, and they showed a result without saying what to notice in it.

The milestones already plan some eighty pages, under `concepts/`, `guides/`, `reference/` and `tool/`. They use the
names without a rule that says what goes where. So each milestone would decide its own layout, and the site would
grow the way its first four pages did.

[Diátaxis](https://diataxis.fr/) names four needs a reader brings to documentation. A **tutorial** teaches by
doing. A **how-to guide** solves one task for someone who already knows what they want. The **reference** describes
the machinery, to look things up. An **explanation** says why things are the way they are. Each need asks for a
different kind of writing, and a page that mixes them serves none of them well.

## Decision

**We will write the user documentation in the four modes of Diátaxis, one mode per page, each mode in its own
section.**

- **Sections.** `tutorials/`, `guides/`, `reference/` and `concepts/`, shown in the sidebar as *Tutorials*,
  *How-to guides*, *Reference* and *Explanation*, in that order. These are the directory names the milestones
  already use, so their plans stay right and the explanation pages keep their addresses. The introduction and
  *Features and comparison* come first, outside the four.
- **One mode per page.** A page that mixes modes is split, and the parts link to each other. A reference page
  describes and does not argue. A guide solves its task and links to the reason. An explanation gives the reason and
  links to the table. A subject usually has a page in several sections under the same name —
  `concepts/reader-limits.md` and `reference/reader-limits.md` —, so that each can link to the other.
- **A tutorial is held to its output.** A tutorial promises an exact result at every step. So its code is a sample
  under `samples/`, compiled with the solution, and a unit test holds the tutorial to that sample. Every C# block of
  the tutorial must be in the sample, and every output it shows must be what the sample prints. The first
  tutorial writes the PDF it opens, so it needs nothing to download and prints the same thing on every machine.
  How-to guides and explanations are not compiled; their snippets are short and point to the reference.
- **The reference holds the public contract.** The rule identifiers of the validator are public API, so their
  table moves from the project documents into the reference, `reference/validation-rules.md`, and is versioned with
  each release like the rest. The generated API reference moves under it too, to `reference/api`.
- **Addresses that moved still work.** `@docusaurus/plugin-client-redirects` redirects `/api/*` and
  `/project/validation-rules` to their new addresses in the built site.
- **The milestones follow.** Each milestone's *Documentation* section names its pages by section. A milestone that
  adds public API writes a reference page, or extends one, for what a caller looks up; a guide for each task that
  API makes possible; an explanation for each design choice a user has to understand; and a tutorial when it opens a
  path a newcomer should learn by doing. Its exit criteria say so, and so does point 5 of the definition of done.

The project documents — the roadmap, the status, the decisions, the milestones — are not user documentation, and
stay as they are, published under `/project`.

## Consequences

- A reader who knows what they need finds it by section: a code in the reference, a task in the guides.
- Some text is written twice, in the words each mode needs. The table of limits is reference, and the reason
  for them is explanation, and each links to the other. When a limit changes, both pages change with it.
- The introduction no longer shows code. It says what each section is for and sends a newcomer to the tutorial.
- The tutorial cannot drift silently. A diagnostic message that changes wording fails a unit test, and the tutorial
  is updated in the same pull request.
- The table of validation rules is now frozen with each stable release. A caller on an older line reads the rules
  of their release. The documents that cite the table cite its new path, and `ValidationRuleIdTests` reads it there.
- The site gains a dependency, `@docusaurus/plugin-client-redirects`, from the same release line as Docusaurus.
- The CLI's pages, planned under `tool/`, are split too: the commands and their options go under `reference/tool/`,
  and the tasks under `guides/`.

### Rejected alternatives

- **Diátaxis's own names for the directories**: `how-to/` and `explanation/`. They would match the vocabulary
  exactly. But every current explanation page would change address, and thirty milestone files would need their
  planned paths rewritten, for names the sidebar shows anyway.
- **Moving the pages into sections without splitting them.** It is quick, but the pages would stay mixed, and
  mixed pages are the problem this decision exists to solve.
- **A tutorial on any PDF the reader has at hand.** Its output would differ from one file to the next, so it could
  promise nothing. A tutorial that downloads a document from the repository would depend on the network and on a
  path.
- **Compiling every snippet of every page.** No snippet could drift. But it would need extraction machinery to
  write and maintain, for guides whose snippets are a few lines long and point to the reference.
- **Keeping the rules table in the project documents.** No path would move, but a caller on a stable release would
  read the rules of `main`.
- **Moving the pages without redirects.** The site has only served previews so far, but its addresses have been
  published and linked since September.
- **Applying Diátaxis to the project documents too.** They are working documents for the maintainer and the
  sessions, and `CLAUDE.md` gives the order to read them in. Reorganizing them would be a different decision.

### What would reopen it

A section that stays empty or near-empty once the library can write, assemble and generate. Or a reader's
complaint, backed by examples, that the split scatters what they need across too many pages.
