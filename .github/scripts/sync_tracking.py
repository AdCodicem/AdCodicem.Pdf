#!/usr/bin/env python3
"""Mirrors the roadmap onto GitHub: its milestones, the labels issues carry, a discussion per open question.

docs/roadmap.md stays the reference for what the milestones are and where each stands — FeatureTablesTests
reads its index table, and CI never asks GitHub. This script makes GitHub say the same thing, so that issues
can be filed under a milestone and its progress read there, without a second list kept by hand.

- milestones: one GitHub milestone per row of the roadmap's index table, titled "M02 — Document validation",
  described by the goal its specification states, closed when the row says "done" and open otherwise. It
  never sets a due date: the roadmap is an intention, not a promise. A milestone the table no longer names is
  reported, never deleted.
- labels: those .github/labels.json defines, created or brought back to their color and description; any
  other label is left alone.
- discussions: one discussion in the Ideas category for each row of the roadmap's "Open questions" table that
  has none, matched by title. An existing discussion is never edited, moved or closed.

    python3 sync_tracking.py labels milestones      what the Tracking workflow runs on every change to main
    python3 sync_tracking.py discussions            from the workflow's manual dispatch
    python3 sync_tracking.py --dry-run milestones   say what would change, change nothing

GITHUB_REPOSITORY (owner/name) and GITHUB_TOKEN come from the environment. A dry run without a token plans
against a repository that has nothing yet. Only the standard library is used, so the job installs nothing.
"""

from __future__ import annotations

import json
import os
import re
import sys
import urllib.error
import urllib.parse
import urllib.request
from dataclasses import dataclass
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
ROADMAP = ROOT / "docs" / "roadmap.md"
MILESTONES = ROOT / "docs" / "milestones"
LABELS = ROOT / ".github" / "labels.json"
API = "https://api.github.com"
IDEAS = "Ideas"

# The same shape FeatureTablesTests reads: identifier, name, size, dependencies, state.
ROADMAP_ROW = re.compile(
    r"^\| (?P<id>M\d+) \| (?P<name>.+?) \| (?P<size>S|M|L|XL) \| (?P<depends>.*?) \| "
    r"(?P<state>done|in progress|to do) \|$")
# Subject and trigger; a third column, the discussion's link, is the roadmap's own and is not read.
OPEN_QUESTION_ROW = re.compile(r"^\| (?P<subject>[^|]+?) \| (?P<trigger>[^|]+?) \|(?: [^|]*? \|)?$")
NEXT_LINK = re.compile(r'<([^>]+)>;\s*rel="next"')


@dataclass(frozen=True)
class RoadmapMilestone:
    id: str
    name: str
    size: str
    depends: str
    state: str

    @property
    def title(self) -> str:
        return f"{self.id} — {self.name}"


@dataclass(frozen=True)
class OpenQuestion:
    subject: str
    trigger: str


@dataclass(frozen=True)
class Action:
    """One change to make on GitHub. `number` names what is updated; `fields` is what changes."""

    kind: str  # "create", "update", or "orphan" (reported only)
    what: str
    number: int | None = None
    fields: dict | None = None


# --- Reading the repository -----------------------------------------------------------------------------


def read_roadmap(text: str) -> list[RoadmapMilestone]:
    """The rows of the roadmap's index table, in order."""
    milestones = []
    for line in text.splitlines():
        row = ROADMAP_ROW.match(line)
        if row:
            milestones.append(RoadmapMilestone(**row.groupdict()))
    return milestones


def read_goal(text: str, heading: str) -> str | None:
    """The first paragraph after a heading line, joined into one line; None when the heading is absent."""
    lines = text.splitlines()
    for index, line in enumerate(lines):
        if line.strip() == heading:
            paragraph = []
            for following in lines[index + 1:]:
                if following.strip():
                    paragraph.append(following.strip())
                elif paragraph:
                    break
            return " ".join(paragraph) or None
    return None


def read_roadmap_goal(text: str, milestone: RoadmapMilestone) -> str | None:
    """The **Goal** line of the milestone's section of the roadmap, for a milestone with no specification."""
    lines = text.splitlines()
    try:
        start = lines.index(f"## {milestone.title}")
    except ValueError:
        return None
    goal = []
    for line in lines[start + 1:]:
        if line.startswith("## "):
            break
        if line.startswith("**Goal**:"):
            goal.append(line[len("**Goal**:"):].strip())
        elif goal and line.strip() and not line.startswith("**"):
            goal.append(line.strip())
        elif goal:
            break
    if not goal:
        return None
    sentence = " ".join(goal)
    return sentence[:1].upper() + sentence[1:]


def goal_of(milestone: RoadmapMilestone, roadmap: str, specifications: Path) -> str | None:
    """The goal the milestone's specification states, or else the one its roadmap section states."""
    specification = specifications / f"{milestone.id}.md"
    if specification.is_file():
        goal = read_goal(specification.read_text(encoding="utf-8"), "## Goal")
        if goal:
            return goal
    return read_roadmap_goal(roadmap, milestone)


def read_open_questions(text: str) -> list[OpenQuestion]:
    """The rows of the roadmap's "Open questions" table, header and rule excluded."""
    questions = []
    inside = False
    for line in text.splitlines():
        if line.startswith("## "):
            inside = line.strip() == "## Open questions"
            continue
        if not inside:
            continue
        row = OPEN_QUESTION_ROW.match(line)
        if row and row["subject"] != "Subject" and not row["subject"].startswith("---"):
            questions.append(OpenQuestion(row["subject"].strip(), row["trigger"].strip()))
    return questions


# --- Deciding what to change ----------------------------------------------------------------------------


def milestone_description(milestone: RoadmapMilestone, goal: str | None, repository: str,
                          has_specification: bool) -> str:
    depends = "nothing" if milestone.depends.strip() in ("", "—", "-") else milestone.depends.strip()
    source = (f"https://github.com/{repository}/blob/main/docs/milestones/{milestone.id}.md" if has_specification
              else f"https://github.com/{repository}/blob/main/docs/roadmap.md#{anchor(milestone.title)}")
    parts = []
    if goal:
        parts.append(goal)
    parts.append(f"Size {milestone.size}, depends on {depends}. Specification: {source}")
    parts.append("Mirrored from docs/roadmap.md by the Tracking workflow: change the roadmap, not this milestone.")
    return "\n\n".join(parts)


def anchor(heading: str) -> str:
    """The fragment GitHub gives a Markdown heading: lower case, punctuation dropped, each space a hyphen."""
    return "".join(c for c in heading.lower() if c.isalnum() or c in " -_").replace(" ", "-")


def milestone_id(title: str) -> str:
    """The identifier a milestone's title starts with: "M02" for "M02 — Document validation"."""
    return title.split(" ", 1)[0]


def plan_milestones(wanted: list[tuple[RoadmapMilestone, str]], existing: list[dict]) -> list[Action]:
    """What makes GitHub's milestones match the roadmap: each wanted milestone paired with its description."""
    by_id: dict[str, list[dict]] = {}
    for milestone in existing:
        by_id.setdefault(milestone_id(milestone["title"]), []).append(milestone)

    actions = []
    for milestone, description in wanted:
        state = "closed" if milestone.state == "done" else "open"
        matches = by_id.pop(milestone.id, [])
        if len(matches) > 1:
            numbers = ", ".join(str(match["number"]) for match in matches)
            raise SystemExit(f"{milestone.id}: several GitHub milestones claim it ({numbers}); keep one by hand.")
        if not matches:
            actions.append(Action("create", milestone.title,
                                  fields={"title": milestone.title, "description": description, "state": state}))
            continue
        current = matches[0]
        changes = {}
        if current["title"] != milestone.title:
            changes["title"] = milestone.title
        if (current.get("description") or "") != description:
            changes["description"] = description
        if current["state"] != state:
            changes["state"] = state
        if changes:
            actions.append(Action("update", milestone.title, number=current["number"], fields=changes))

    for leftovers in by_id.values():
        for milestone in leftovers:
            actions.append(Action("orphan", milestone["title"], number=milestone["number"]))
    return actions


def plan_labels(wanted: list[dict], existing: list[dict]) -> list[Action]:
    """What gives each label of .github/labels.json its color and description, touching no other label."""
    by_name = {label["name"].lower(): label for label in existing}
    actions = []
    for label in wanted:
        current = by_name.get(label["name"].lower())
        if current is None:
            actions.append(Action("create", label["name"], fields=dict(label)))
            continue
        changes = {}
        if current["name"] != label["name"]:
            changes["new_name"] = label["name"]
        if current["color"].lower() != label["color"].lower():
            changes["color"] = label["color"]
        if (current.get("description") or "") != label["description"]:
            changes["description"] = label["description"]
        if changes:
            actions.append(Action("update", current["name"], fields=changes))
    return actions


def discussion_body(question: OpenQuestion, repository: str) -> str:
    roadmap = f"https://github.com/{repository}/blob/main/docs/roadmap.md#open-questions"
    return (
        f"An [open question of the roadmap]({roadmap}): neither planned nor excluded.\n\n"
        f"**What would trigger it**: {question.trigger}.\n\n"
        "If you need it, say so here — with the document or the task that asks for it; an upvote counts too. "
        "Real use is what moves an open question into a milestone. When it does, an issue is opened under "
        "that milestone and linked from here, and the roadmap names the milestone.")


def plan_discussions(questions: list[OpenQuestion], existing_titles: set[str], repository: str) -> list[Action]:
    """A discussion for each open question that has none, matched by title."""
    known = {title.casefold() for title in existing_titles}
    return [Action("create", question.subject,
                   fields={"title": question.subject, "body": discussion_body(question, repository)})
            for question in questions if question.subject.casefold() not in known]


# --- Talking to GitHub ----------------------------------------------------------------------------------


class GitHub:
    def __init__(self, repository: str, token: str | None):
        self.repository = repository
        self.token = token

    def _send(self, method: str, url: str, body: dict | None = None) -> tuple[object, dict]:
        request = urllib.request.Request(url, method=method,
                                         data=json.dumps(body).encode("utf-8") if body is not None else None)
        request.add_header("Accept", "application/vnd.github+json")
        request.add_header("X-GitHub-Api-Version", "2022-11-28")
        request.add_header("User-Agent", "AdCodicem.Pdf-tracking")
        if self.token:
            request.add_header("Authorization", f"Bearer {self.token}")
        if body is not None:
            request.add_header("Content-Type", "application/json")
        try:
            with urllib.request.urlopen(request, timeout=60) as response:
                payload = response.read()
                return (json.loads(payload) if payload else None), dict(response.headers)
        except urllib.error.HTTPError as error:
            detail = error.read().decode("utf-8", "replace")
            raise SystemExit(f"{method} {url}: HTTP {error.code}: {detail}") from None

    def rest(self, method: str, path: str, body: dict | None = None) -> object:
        return self._send(method, f"{API}/repos/{self.repository}{path}", body)[0]

    def all(self, path: str) -> list[dict]:
        url = f"{API}/repos/{self.repository}{path}"
        items: list[dict] = []
        while url:
            page, headers = self._send("GET", url)
            items.extend(page)
            link = NEXT_LINK.search(headers.get("Link") or headers.get("link") or "")
            url = link.group(1) if link else None
        return items

    def graphql(self, query: str, variables: dict) -> dict:
        result, _ = self._send("POST", f"{API}/graphql", {"query": query, "variables": variables})
        if result.get("errors"):
            raise SystemExit(f"GraphQL: {json.dumps(result['errors'])}")
        return result["data"]


DISCUSSION_CONTEXT = """
query($owner: String!, $name: String!, $after: String) {
  repository(owner: $owner, name: $name) {
    id
    discussionCategories(first: 50) { nodes { id name } }
    discussions(first: 100, after: $after) {
      nodes { title }
      pageInfo { hasNextPage endCursor }
    }
  }
}"""

CREATE_DISCUSSION = """
mutation($repository: ID!, $category: ID!, $title: String!, $body: String!) {
  createDiscussion(input: {repositoryId: $repository, categoryId: $category, title: $title, body: $body}) {
    discussion { number url }
  }
}"""


# --- The three commands ---------------------------------------------------------------------------------


def report(line: str, summary: list[str]) -> None:
    print(line)
    summary.append(f"- {line}")


def sync_milestones(github: GitHub | None, repository: str, dry_run: bool, summary: list[str]) -> None:
    roadmap = ROADMAP.read_text(encoding="utf-8")
    wanted = []
    for milestone in read_roadmap(roadmap):
        goal = goal_of(milestone, roadmap, MILESTONES)
        has_specification = (MILESTONES / f"{milestone.id}.md").is_file()
        wanted.append((milestone, milestone_description(milestone, goal, repository, has_specification)))
    if not wanted:
        raise SystemExit("docs/roadmap.md: no milestone found in the index table; its format changed.")

    existing = github.all("/milestones?state=all&per_page=100") if github else []
    actions = plan_milestones(wanted, existing)
    for action in actions:
        if action.kind == "orphan":
            report(f"milestone #{action.number} \"{action.what}\" is not in the roadmap: left as it is", summary)
            continue
        report(f"milestone {action.kind}: {action.what} ({', '.join(sorted(action.fields))})", summary)
        if dry_run:
            continue
        if action.kind == "create":
            created = github.rest("POST", "/milestones", action.fields)
            report(f"  created as milestone #{created['number']}", summary)
        else:
            github.rest("PATCH", f"/milestones/{action.number}", action.fields)
    if not actions:
        report(f"milestones: all {len(wanted)} match the roadmap", summary)
    if github and not dry_run:
        numbers = {milestone_id(m["title"]): m["number"] for m in github.all("/milestones?state=all&per_page=100")}
        summary.append("")
        summary.append("| Milestone | Number |")
        summary.append("|---|---|")
        for milestone, _ in wanted:
            summary.append(f"| {milestone.id} | {numbers.get(milestone.id, '—')} |")


def sync_labels(github: GitHub | None, dry_run: bool, summary: list[str]) -> None:
    wanted = json.loads(LABELS.read_text(encoding="utf-8"))
    existing = github.all("/labels?per_page=100") if github else []
    actions = plan_labels(wanted, existing)
    for action in actions:
        report(f"label {action.kind}: {action.what} ({', '.join(sorted(action.fields))})", summary)
        if dry_run:
            continue
        if action.kind == "create":
            github.rest("POST", "/labels", action.fields)
        else:
            github.rest("PATCH", f"/labels/{urllib.parse.quote(action.what, safe='')}", action.fields)
    if not actions:
        report(f"labels: all {len(wanted)} match .github/labels.json", summary)


def sync_discussions(github: GitHub | None, repository: str, dry_run: bool, summary: list[str]) -> None:
    questions = read_open_questions(ROADMAP.read_text(encoding="utf-8"))
    if not questions:
        raise SystemExit("docs/roadmap.md: no row found under \"## Open questions\"; its format changed.")

    titles: set[str] = set()
    repository_id = category_id = None
    if github and github.token:
        owner, name = repository.split("/", 1)
        after = None
        while True:
            data = github.graphql(DISCUSSION_CONTEXT, {"owner": owner, "name": name, "after": after})["repository"]
            repository_id = data["id"]
            categories = {node["name"].casefold(): node["id"] for node in data["discussionCategories"]["nodes"]}
            category_id = categories.get(IDEAS.casefold())
            titles.update(node["title"] for node in data["discussions"]["nodes"])
            if not data["discussions"]["pageInfo"]["hasNextPage"]:
                break
            after = data["discussions"]["pageInfo"]["endCursor"]
        if category_id is None:
            raise SystemExit(f"No discussion category named {IDEAS}: create it in the repository's settings.")
    elif not dry_run:
        raise SystemExit("Creating discussions needs GITHUB_TOKEN.")

    actions = plan_discussions(questions, titles, repository)
    for action in actions:
        report(f"discussion create: {action.what}", summary)
        if dry_run:
            continue
        created = github.graphql(CREATE_DISCUSSION, {"repository": repository_id, "category": category_id,
                                                     "title": action.fields["title"], "body": action.fields["body"]})
        discussion = created["createDiscussion"]["discussion"]
        report(f"  opened as #{discussion['number']}: {discussion['url']}", summary)
    if not actions:
        report(f"discussions: all {len(questions)} open questions have one", summary)


def main(arguments: list[str]) -> int:
    dry_run = "--dry-run" in arguments
    commands = [argument for argument in arguments if argument != "--dry-run"]
    unknown = [command for command in commands if command not in ("labels", "milestones", "discussions")]
    if not commands or unknown:
        print(__doc__, file=sys.stderr)
        return 2

    repository = os.environ.get("GITHUB_REPOSITORY", "AdCodicem/AdCodicem.Pdf")
    token = os.environ.get("GITHUB_TOKEN")
    if not token and not dry_run:
        print("GITHUB_TOKEN is required unless --dry-run is given.", file=sys.stderr)
        return 2
    github = GitHub(repository, token) if token else None

    summary: list[str] = [f"## Tracking{' (dry run)' if dry_run else ''}", ""]
    for command in commands:
        if command == "labels":
            sync_labels(github, dry_run, summary)
        elif command == "milestones":
            sync_milestones(github, repository, dry_run, summary)
        else:
            sync_discussions(github, repository, dry_run, summary)

    step_summary = os.environ.get("GITHUB_STEP_SUMMARY")
    if step_summary:
        with open(step_summary, "a", encoding="utf-8") as output:
            output.write("\n".join(summary) + "\n")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
