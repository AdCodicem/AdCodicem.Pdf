"""What sync_tracking.py reads from the repository and decides to change on GitHub, without GitHub.

The script's writes go through one small client; everything before them — the roadmap's two tables, each
milestone's goal, and the plan that makes GitHub match — is checked here on every change, against the real
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
from sync_tracking import OpenQuestion, RoadmapMilestone  # noqa: E402

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


class Arguments(unittest.TestCase):
    def test_refuses_an_unknown_command(self):
        self.assertEqual(sync_tracking.main(["--dry-run", "issues"]), 2)

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
