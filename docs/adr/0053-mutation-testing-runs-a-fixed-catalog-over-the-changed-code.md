# 53. Mutation testing runs a fixed catalog over the code a pull request changes

Date: 2026-10-30

## Status

Accepted on 2026-10-30, on the maintainer's four decisions of that day, taken during batch 17
([#155](https://github.com/AdCodicem/AdCodicem.Pdf/issues/155)) once its review had run past thirty rounds. It
generalizes the bound set that morning for that batch's review, with one level less: no helper specialized at each of
its callers.

## Context

Since batch 5 each batch has mutated the defenses it adds: each mutant is built, the tests of its classes are run on
it, and each survivor is killed by a test written for it or shown equivalent. `docs/status.md` records the counts,
from 8 to 152 mutants a batch. The code mutated was the batch's own, the operators were the usual ones, and the step
closed in one round, or in a few, one for each round of fixes.

Batch 17 added a test of another kind. `SeededComparerTests` reads the compiled library and refuses every hash
collection built over a key a file can steer without the seeded comparer. It is an oracle over the library's code,
and its domain is open: every way .NET builds, copies or returns a hash table. Its review ran until a round found no
minor finding, and mutation became the review's main source of findings:

- **The code mutated grew with each fix.** Each round's fixes were samples and stand-ins written to kill the last
  round's survivors, and the next round mutated the guard they had changed.
- **The catalog grew with each round.** It started with the usual operators. Then it dropped each element of each set
  the guard reads. Then it specialized each helper at each of its call sites. The next step would have been one call
  path and not another, or two conditions at once. Each refinement surfaced survivors the previous one could not.
- **Each survivor counted as a minor finding,** so each one restarted the stopping rule.

The last four sweeps, rounds 28 to 31, built 415, 521, 492 and 642 mutants and raised 25 minor findings. All were
gaps of the guard. No commit after 28 October changed `src/`: the rounds that followed changed only the guard and
the documents. On 2026-10-30 the maintainer bounded the catalog for that batch, and a last round was run only to
confirm the bound.

## Decision

**Mutation testing is one finite step of a pull request's verification, apart from its review. A fixed, first-order
catalog is run over the code the pull request changes, and each survivor is killed or classed equivalent.**

- **What is mutated.** The lines of `src/` the pull request changes. A test is mutated only when it is an oracle over
  the library's code: a test that reads the library's sources or compiled code and refuses a shape there, such as
  `SeededComparerTests`. Even then, only the conditions the pull request adds to it or changes in it are mutated: the
  reading's conditions, the arms of its helpers, the elements of the sets it reads. The samples, stand-ins and tests
  written to kill a mutant are never mutated in their turn.
- **The catalog.** Each mutant makes one change, at one place the code writes it:
  1. negate a condition, or replace it with `true` or `false`;
  2. shift a bound by one: `<` and `<=`, `>` and `>=`, a constant `n` and `n ± 1`;
  3. remove a call, or the statement that makes it;
  4. return the default value from a member: `false`, `0`, `null`, an empty collection;
  5. remove one element of a set the code reads (an array, a list of names, an alternative of a pattern), or one arm
     of a switch.

  A helper is mutated once, in its body, and not specialized at each of its callers. No mutant changes the code along
  one call path and not another. No mutant makes two changes at once.
- **What a survivor is.** A mutant no test fails is a gap in the tests, not a defect of the library. It is settled in
  the step. Either a test is written that fails on it, or it is classed equivalent, with one line saying why no input
  tells it apart. An equivalent, once classed, is not reopened by a later round. A survivor is not a review finding:
  it does not count as a minor finding, and it does not restart the review.
- **When the step ends.** After the fixes, only the survivors are run again. The step ends when every mutant of the
  catalog is killed or classed equivalent. If a later fix changes `src/`, the catalog is run over the lines it changes,
  and those alone.
- **What is recorded.** The pull request's entry in `docs/status.md` gives the number of mutants, how many were
  killed, and each equivalent with its reason.

## Consequences

- The step is bounded: the size of the patch times a fixed catalog, then the survivors once more. It ends however many
  tests its fixes add, because those tests are not mutated.
- The review's stopping rule counts only what the review finds in the library, its tests and its documents. A survivor
  no longer costs a review round.
- A first-order catalog misses a defect that shows only when two conditions change together, or along one caller and
  not another. That is accepted. Tests that kill every first-order mutant kill most higher-order ones (the coupling
  effect, Offutt, *Investigations of the software testing coupling effect*, ACM TOSEM, 1992). And the review reads the
  code paths that mutation no longer walks one by one. It is also what common mutation tools do, Stryker.NET and PIT
  among them: they mutate each place in the source once.
- Mutation no longer searches an oracle's open domain. A form of construction the guard does not read changes no
  condition of it, so no mutant reveals it. Finding it is the review's job, as for any other code.
- An equivalent is a judgment, and it can be wrong. Its written reason makes it checkable. A test that tells the
  mutant apart from the original is the only thing that shows it was not equivalent. Such a test is new evidence: it
  is added, not argued over.

### Alternatives rejected

- **Mutating everything the pull request changes, tests included.** Each test written to kill a mutant becomes code to
  mutate, and the step has no fixed point. That is batch 17's regress.
- **Mutating `src/` only, never a test.** It is the shortest step. But a guard that reads the library, and lets a
  whole class of defect through if one of its conditions is wrong, would hold nothing on its own conditions.
- **The catalog with each helper specialized at each call site.** This is the bound set for batch 17, generalized. It
  builds more mutants, and its survivors are ones only the specialization shows. Batch 17's 31st round found 8 minor
  findings there, all in the guard and none in the library.
- **A catalog chosen when each batch starts, by its risk.** Each batch would reopen the question this record settles.
- **A survivor counted as a minor finding, with the catalog fixed.** This converges, but each survivor costs a full
  review round.
- **Survivors past a threshold filed as debt.** This bounds the cost. But it leaves known gaps in a pull request whose
  claim is that its tests hold its defenses.

## What would reopen it

Two things. A defect reaching `main` that no review found and that a mutant outside the catalog would have caught: a
second-order mutant, or a helper specialized at one of its callers. Or the step costing a pull request more than its
review does.
