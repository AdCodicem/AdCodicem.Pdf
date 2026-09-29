"""What sync_tracking.py reads from the repository and decides to change on GitHub, without GitHub.

The script's writes go through one small client; everything before them — the roadmap's two tables, each
milestone's goal, the dependencies issue bodies declare, and the plan that makes GitHub match — is checked here on every change, against the real
roadmap as well as against small texts, so that a format change that would silently mirror nothing fails CI.

    python3 -m unittest discover -s .github/scripts -p "test_*.py"
"""

from __future__ import annotations

import json
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import sync_tracking  # noqa: E402
from sync_tracking import Dependency, OpenQuestion, RoadmapMilestone  # noqa: E402

REPOSITORY = "AdCodicem/AdCodicem.Pdf"

ROADMAP = """# Roadmap

| # | Milestone | Size | Depends on | State |
|---|-----------|------|------------|-------|
| M00 | Repository foundations | S | — | done |
| M02 | Document validation | M | M01 | in progress |
| M12 | HTML → PDF engine | XL | M06, M07 | to do |

## M00 — Repository foundations

**Goal**: any session can build, test and publish
without discovering anything.
**Deliverables**: solution and project layout.

## M02 — Document validation

**Deliverables**: a rule engine.

## Open questions

Neither planned nor excluded.

| Subject | What would trigger it |
|---|---|
| Variable fonts, instanced before subsetting | A brand font delivered only as a variable font |
| A code-first layout API beside HTML (ADR 8) | Callers asking for one over templates |

## Renumbering of 2026-09-26

| Before | After |
|---|---|
| M0 to M3 | M00 to M03 |
"""


def milestone(id_: str = "M02", name: str = "Document validation", state: str = "in progress") -> RoadmapMilestone:
    return RoadmapMilestone(id_, name, "M", "M01", state)


class ReadingTheRoadmap(unittest.TestCase):
    def test_reads_every_row_of_the_index_table_in_order(self):
        milestones = sync_tracking.read_roadmap(ROADMAP)

        self.assertEqual([m.id for m in milestones], ["M00", "M02", "M12"])
        self.assertEqual(milestones[2], RoadmapMilestone("M12", "HTML → PDF engine", "XL", "M06, M07", "to do"))
        self.assertEqual(milestones[1].title, "M02 — Document validation")

    def test_reads_only_the_open_questions_table(self):
        questions = sync_tracking.read_open_questions(ROADMAP)

        self.assertEqual(questions, [
            OpenQuestion("Variable fonts, instanced before subsetting", "A brand font delivered only as a variable font"),
            OpenQuestion("A code-first layout API beside HTML (ADR 8)", "Callers asking for one over templates"),
        ])

    def test_reads_an_open_question_whatever_links_its_discussion(self):
        text = ("## Open questions\n\n| Subject | What would trigger it | Discussion |\n|---|---|---|\n"
                "| Color fonts and emoji | Chat transcripts as case-file pieces | [#92] |\n")

        self.assertEqual(sync_tracking.read_open_questions(text),
                         [OpenQuestion("Color fonts and emoji", "Chat transcripts as case-file pieces")])

    def test_takes_a_goal_from_the_roadmap_section_joined_and_capitalized(self):
        goal = sync_tracking.read_roadmap_goal(ROADMAP, milestone("M00", "Repository foundations", "done"))

        self.assertEqual(goal, "Any session can build, test and publish without discovering anything.")

    def test_has_no_goal_for_a_section_without_one(self):
        self.assertIsNone(sync_tracking.read_roadmap_goal(ROADMAP, milestone()))

    def test_prefers_the_goal_the_specification_states(self):
        with tempfile.TemporaryDirectory() as directory:
            specifications = Path(directory)
            (specifications / "M00.md").write_text(
                "# M00\n\n## Goal\n\nBuild and test\nfrom a clean clone.\n\n## Scope\n\nAll.\n", encoding="utf-8")

            goal = sync_tracking.goal_of(milestone("M00", "Repository foundations", "done"), ROADMAP, specifications)

        self.assertEqual(goal, "Build and test from a clean clone.")

    def test_falls_back_on_the_roadmap_when_there_is_no_specification(self):
        with tempfile.TemporaryDirectory() as directory:
            goal = sync_tracking.goal_of(milestone("M00", "Repository foundations", "done"), ROADMAP, Path(directory))

        self.assertEqual(goal, "Any session can build, test and publish without discovering anything.")


class TheRealRoadmap(unittest.TestCase):
    """The repository's own documents, so that a change of format fails here rather than mirroring nothing."""

    def test_names_every_milestone_with_a_state_and_a_goal(self):
        roadmap = sync_tracking.ROADMAP.read_text(encoding="utf-8")
        milestones = sync_tracking.read_roadmap(roadmap)

        self.assertEqual(milestones[0].id, "M00")
        self.assertEqual(len({m.id for m in milestones}), len(milestones))
        self.assertGreaterEqual(len(milestones), 32)
        for m in milestones:
            with self.subTest(m.id):
                self.assertTrue(sync_tracking.goal_of(m, roadmap, sync_tracking.MILESTONES))

    def test_has_open_questions(self):
        questions = sync_tracking.read_open_questions(sync_tracking.ROADMAP.read_text(encoding="utf-8"))

        self.assertTrue(questions)
        self.assertNotIn("Subject", [q.subject for q in questions])

    def test_defines_labels_github_accepts(self):
        labels = json.loads(sync_tracking.LABELS.read_text(encoding="utf-8"))

        self.assertEqual(len({label["name"].lower() for label in labels}), len(labels))
        for label in labels:
            with self.subTest(label["name"]):
                self.assertRegex(label["color"], r"^[0-9a-f]{6}$")
                self.assertLessEqual(len(label["description"]), 100)
                self.assertLessEqual(len(label["name"]), 50)


class PlanningMilestones(unittest.TestCase):
    def test_creates_a_missing_milestone_closed_when_done_and_open_otherwise(self):
        wanted = [(milestone("M01", "Object model", "done"), "d1"), (milestone(), "d2")]

        actions = sync_tracking.plan_milestones(wanted, [])

        self.assertEqual([(a.kind, a.fields["title"], a.fields["state"]) for a in actions], [
            ("create", "M01 — Object model", "closed"),
            ("create", "M02 — Document validation", "open"),
        ])

    def test_never_sets_a_due_date(self):
        actions = sync_tracking.plan_milestones([(milestone(), "d")], [])

        self.assertNotIn("due_on", actions[0].fields)

    def test_leaves_a_milestone_that_matches_alone(self):
        existing = [{"number": 3, "title": "M02 — Document validation", "description": "d", "state": "open"}]

        self.assertEqual(sync_tracking.plan_milestones([(milestone(), "d")], existing), [])

    def test_changes_only_what_differs(self):
        existing = [{"number": 3, "title": "M02 — Validation", "description": "d", "state": "open"}]

        actions = sync_tracking.plan_milestones([(milestone(state="done"), "d")], existing)

        self.assertEqual(len(actions), 1)
        self.assertEqual((actions[0].kind, actions[0].number), ("update", 3))
        self.assertEqual(actions[0].fields, {"title": "M02 — Document validation", "state": "closed"})

    def test_reopens_a_milestone_the_roadmap_takes_back(self):
        existing = [{"number": 3, "title": "M02 — Document validation", "description": "d", "state": "closed"}]

        actions = sync_tracking.plan_milestones([(milestone(), "d")], existing)

        self.assertEqual(actions[0].fields, {"state": "open"})

    def test_reports_a_milestone_the_roadmap_does_not_name_without_touching_it(self):
        existing = [{"number": 9, "title": "Someday", "description": None, "state": "open"}]

        actions = sync_tracking.plan_milestones([(milestone(), "d")], existing)

        self.assertEqual([(a.kind, a.number) for a in actions], [("create", None), ("orphan", 9)])
        self.assertIsNone(actions[1].fields)

    def test_refuses_two_milestones_that_claim_one_identifier(self):
        existing = [
            {"number": 3, "title": "M02 — Document validation", "description": "d", "state": "open"},
            {"number": 4, "title": "M02 — Validation", "description": "d", "state": "open"},
        ]

        with self.assertRaises(SystemExit):
            sync_tracking.plan_milestones([(milestone(), "d")], existing)

    def test_describes_a_milestone_by_its_goal_size_dependencies_and_specification(self):
        description = sync_tracking.milestone_description(milestone(), "Say what is wrong.", REPOSITORY, True)

        self.assertTrue(description.startswith("Say what is wrong.\n\nSize M, depends on M01."))
        self.assertIn(f"https://github.com/{REPOSITORY}/blob/main/docs/milestones/M02.md", description)
        self.assertIn("change the roadmap, not this milestone", description)

    def test_points_at_the_roadmap_section_when_there_is_no_specification(self):
        foundations = RoadmapMilestone("M00", "Repository foundations", "S", "—", "done")

        description = sync_tracking.milestone_description(foundations, None, REPOSITORY, False)

        self.assertTrue(description.startswith("Size S, depends on nothing."))
        self.assertIn("docs/roadmap.md#m00--repository-foundations", description)


class PlanningLabels(unittest.TestCase):
    WANTED = [{"name": "debt", "color": "d93f0b", "description": "Known debt"}]

    def test_creates_a_missing_label(self):
        actions = sync_tracking.plan_labels(self.WANTED, [])

        self.assertEqual([(a.kind, a.fields) for a in actions], [("create", self.WANTED[0])])

    def test_restores_color_description_and_case_and_leaves_other_labels_alone(self):
        existing = [
            {"name": "Debt", "color": "ededed", "description": None},
            {"name": "bug", "color": "d73a4a", "description": "Something isn't working"},
        ]

        actions = sync_tracking.plan_labels(self.WANTED, existing)

        self.assertEqual(len(actions), 1)
        self.assertEqual((actions[0].kind, actions[0].what), ("update", "Debt"))
        self.assertEqual(actions[0].fields, {"new_name": "debt", "color": "d93f0b", "description": "Known debt"})

    def test_leaves_a_label_that_matches_alone(self):
        existing = [{"name": "debt", "color": "D93F0B", "description": "Known debt"}]

        self.assertEqual(sync_tracking.plan_labels(self.WANTED, existing), [])


class PlanningDiscussions(unittest.TestCase):
    QUESTIONS = [OpenQuestion("Color fonts and emoji", "Chat transcripts as case-file pieces"),
                 OpenQuestion("Signed French 2D-Doc codes", "An issuer approved by ANTS asking for them")]

    def test_opens_one_for_each_question_that_has_none_whatever_the_case_of_its_title(self):
        actions = sync_tracking.plan_discussions(self.QUESTIONS, {"color FONTS and emoji"}, REPOSITORY)

        self.assertEqual([a.what for a in actions], ["Signed French 2D-Doc codes"])
        self.assertEqual(actions[0].fields["title"], "Signed French 2D-Doc codes")

    def test_says_what_would_trigger_it_and_links_the_roadmap(self):
        body = sync_tracking.discussion_body(self.QUESTIONS[1], REPOSITORY)

        self.assertIn("**What would trigger it**: An issuer approved by ANTS asking for them.", body)
        self.assertIn(f"https://github.com/{REPOSITORY}/blob/main/docs/roadmap.md#open-questions", body)


def issue(number: int, body: str = "", association: str = "OWNER", **extra) -> dict:
    return {"number": number, "id": 1000 + number, "body": body, "author_association": association, **extra}


class ReadingDependencies(unittest.TestCase):
    def test_reads_what_an_issue_is_blocked_by_in_the_order_written(self):
        self.assertEqual(sync_tracking.read_dependencies(60, "Slice 4.\n\nBlocked by: #55, #56, #120\n"),
                         [Dependency(60, 55), Dependency(60, 56), Dependency(60, 120)])

    def test_reads_what_an_issue_blocks_from_the_other_side(self):
        self.assertEqual(sync_tracking.read_dependencies(125, "Blocks: #126"), [Dependency(126, 125)])

    def test_accepts_a_list_item_bold_either_way_and_any_case(self):
        for line in ("- **Blocked by**: #7", "* **Blocked by:** #7", "blocked BY : #7", "  Blocked by:#7"):
            with self.subTest(line=line):
                self.assertEqual(sync_tracking.read_dependencies(8, line), [Dependency(8, 7)])

    def test_skips_a_target_that_is_not_an_issue_yet(self):
        self.assertEqual(sync_tracking.read_dependencies(36, "Blocks: M03 slice 8, M06 slice 5"), [])
        self.assertEqual(sync_tracking.read_dependencies(36, "Blocks: M03 slice 8, #70"), [Dependency(70, 36)])

    def test_reads_neither_prose_nor_fenced_code_nor_a_link_fragment(self):
        body = ("This slice waits on #55, and is blocked by it.\n"
                "```\nBlocked by: #1\n```\n"
                "Blocked by: https://example.org/page#12 and &#13;\n")
        self.assertEqual(sync_tracking.read_dependencies(60, body), [])

    def test_reads_nothing_from_an_empty_body(self):
        self.assertEqual(sync_tracking.read_dependencies(60, None), [])


class PlanningDependencies(unittest.TestCase):
    def test_counts_both_sides_and_one_declaration_on_each_only_once(self):
        wanted, ignored = sync_tracking.wanted_dependencies(
            [issue(55, "Blocks: #60"), issue(56), issue(60, "Blocked by: #55, #56")])
        self.assertEqual(wanted, {Dependency(60, 55), Dependency(60, 56)})
        self.assertEqual(ignored, [])

    def test_ignores_what_someone_outside_the_project_declares(self):
        for association in ("NONE", "CONTRIBUTOR", "FIRST_TIME_CONTRIBUTOR", None):
            with self.subTest(association=association):
                wanted, ignored = sync_tracking.wanted_dependencies(
                    [issue(60), issue(200, "Blocks: #60", association=association)])
                self.assertEqual(wanted, set())
                self.assertIn("#200", ignored[0])

    def test_trusts_members_and_collaborators(self):
        wanted, _ = sync_tracking.wanted_dependencies(
            [issue(1), issue(2, "Blocked by: #1", "MEMBER"), issue(3, "Blocked by: #1", "COLLABORATOR")])
        self.assertEqual(wanted, {Dependency(2, 1), Dependency(3, 1)})

    def test_ignores_itself_a_pull_request_and_a_number_no_issue_has(self):
        wanted, ignored = sync_tracking.wanted_dependencies(
            [issue(60, "Blocked by: #60, #110, #999"), issue(110, pull_request={}),
             issue(111, "Blocked by: #60", pull_request={})])
        self.assertEqual(wanted, set())
        self.assertEqual(ignored, ["#60: names itself", "#60: #110 is not an issue of this repository",
                                   "#60: #999 is not an issue of this repository"])

    def test_adds_what_is_missing_and_removes_what_no_body_declares(self):
        actions = sync_tracking.plan_dependencies({Dependency(60, 55), Dependency(60, 56)},
                                                  {Dependency(60, 56), Dependency(61, 60)})
        self.assertEqual([(a.kind, a.number, a.fields) for a in actions],
                         [("create", 60, {"blocking": 55}), ("delete", 61, {"blocking": 60})])
        self.assertEqual(actions[0].what, "#60 blocked by #55")

    def test_changes_nothing_when_github_matches(self):
        self.assertEqual(sync_tracking.plan_dependencies({Dependency(2, 1)}, {Dependency(2, 1)}), [])


class FakeGitHub:
    """Lists `issues`, answers each issue's blockers from a table, and records what is asked and sent."""

    def __init__(self, issues: list[dict], blocked_by: dict[int, list[dict]]):
        self.repository = REPOSITORY
        self.issues = issues
        self.blocked_by = blocked_by
        self.asked: list[int] = []
        self.sent: list[tuple] = []

    def all(self, path: str) -> list[dict]:
        if path.startswith("/issues?"):
            return self.issues
        number = int(path.split("/")[2])
        self.asked.append(number)
        return self.blocked_by.get(number, [])

    def rest(self, method: str, path: str, body: dict | None = None) -> None:
        self.sent.append((method, path, body))


def blocker(number: int, repository: str = REPOSITORY) -> dict:
    return {"number": number, "repository_url": f"https://api.github.com/repos/{repository}"}


class SyncingDependencies(unittest.TestCase):
    def test_asks_only_the_issues_the_summary_says_are_blocked_and_keeps_other_repositories_out(self):
        github = FakeGitHub([
            issue(55, issue_dependencies_summary={"total_blocked_by": 0}),
            issue(60, issue_dependencies_summary={"total_blocked_by": 2}),
            issue(62),
            issue(122, pull_request={}),
        ], {60: [blocker(55), blocker(9, "someone/else")], 62: [blocker(61)]})
        existing = sync_tracking.existing_dependencies(github, github.issues)
        self.assertEqual(existing, {Dependency(60, 55), Dependency(62, 61)})
        self.assertEqual(github.asked, [60, 62])

    def test_adds_and_removes_through_the_blocking_issues_identifier(self):
        github = FakeGitHub([issue(55), issue(56), issue(60, "Blocked by: #55")], {60: [blocker(56)]})
        summary: list[str] = []
        sync_tracking.sync_dependencies(github, dry_run=False, summary=summary)
        self.assertEqual(github.sent, [
            ("POST", "/issues/60/dependencies/blocked_by", {"issue_id": 1055}),
            ("DELETE", "/issues/60/dependencies/blocked_by/1056", None),
        ])
        self.assertEqual(summary, ["- dependency create: #60 blocked by #55", "- dependency delete: #60 blocked by #56"])

    def test_a_dry_run_changes_nothing(self):
        github = FakeGitHub([issue(55), issue(60, "Blocked by: #55")], {})
        sync_tracking.sync_dependencies(github, dry_run=True, summary=[])
        self.assertEqual(github.sent, [])

    def test_says_so_when_github_already_matches(self):
        github = FakeGitHub([issue(55), issue(60, "Blocked by: #55")], {60: [blocker(55)]})
        summary: list[str] = []
        sync_tracking.sync_dependencies(github, dry_run=False, summary=summary)
        self.assertEqual(github.sent, [])
        self.assertEqual(summary, ["- dependencies: all 1 the issues declare are on GitHub, and no other"])


class Arguments(unittest.TestCase):
    def test_refuses_an_unknown_command(self):
        self.assertEqual(sync_tracking.main(["--dry-run", "issues"]), 2)

    def test_accepts_the_dependencies_command(self):
        environment = dict(sync_tracking.os.environ)
        sync_tracking.os.environ.pop("GITHUB_TOKEN", None)
        sync_tracking.os.environ.pop("GITHUB_STEP_SUMMARY", None)
        try:
            self.assertEqual(sync_tracking.main(["--dry-run", "dependencies"]), 0)
        finally:
            sync_tracking.os.environ.clear()
            sync_tracking.os.environ.update(environment)

    def test_refuses_to_write_without_a_token(self):
        environment = dict(sync_tracking.os.environ)
        sync_tracking.os.environ.pop("GITHUB_TOKEN", None)
        try:
            self.assertEqual(sync_tracking.main(["milestones"]), 2)
        finally:
            sync_tracking.os.environ.clear()
            sync_tracking.os.environ.update(environment)


if __name__ == "__main__":
    unittest.main()
