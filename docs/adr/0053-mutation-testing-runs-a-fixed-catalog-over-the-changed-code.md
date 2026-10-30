# 53. Mutation testing runs a fixed catalog over the code a pull request changes

Date: 2026-10-30

## Status

Accepted on 2026-10-30, on the maintainer's decisions of that day, put to them in two sets of questions during batch
17 ([#155](https://github.com/AdCodicem/AdCodicem.Pdf/issues/155)) once its review had run past thirty rounds. It makes
the bound set that morning for that batch's review the general rule, without its per-call-site level and its
narrowings: a helper is mutated in its body, not at each of its callers, and no condition is narrowed to a sub-case.

## Context

Since batch 5 — the batches are the pull requests in which M02 pays the debt filed under it — each batch has mutated
the defenses it adds: each mutant is built, and the suite, or the tests of its classes, run on it. Nearly every
survivor was killed by a test written for it, or shown equivalent: no input tells it from the original. One, in batch
13, was filed as debt ([#257](https://github.com/AdCodicem/AdCodicem.Pdf/issues/257)) and paid in batch 15.
`docs/status.md` gives the counts, from 8 mutants a batch (batches 6 and 7) to 152 (batch 14). The code mutated was
the batch's own, the operators were chosen for each defense, and the step closed in a round or a few, one for each
round of fixes.

Batch 17 added a test of another kind. `SeededComparerTests` reads the compiled library and refuses every hash
collection built without the seeded comparer over a key a file can steer
([ADR 52](0052-tables-keyed-by-numbers-are-dense-or-seeded.md)). It is an oracle over the library's code: a test that
judges the code itself rather than what the code does. Its domain is open: every way .NET builds, copies or returns a
hash table. Its review ran in rounds, and was to stop at the first round that found no minor finding. Mutation became
that review's main source of findings:

- **The code mutated grew with each fix.** Each gap the review closed added an arm or a set element to the guard
  test's reading, and the next round mutated it.
- **The catalog grew with each round.** It started with the usual operators. Then it dropped each element of each set
  the reading reads, then specialized each helper at each of its call sites, then narrowed each condition to a sub-case
  of it: a record arm to concrete records, an existence over type arguments to its first item. A condition has as many
  sub-cases as the cases it reads, and each refinement surfaced survivors the previous one could not.
- **Each survivor counted as a minor finding,** so each one restarted the stopping rule.

Rounds 28 to 31 built 415, 521, 492 and 642 mutants and raised 25 minor findings, all gaps of the guard test. No
commit after `b83eafd`, on 28 October, changed `src/`: the rounds that followed changed only the guard test and the
documents. On 2026-10-30 the maintainer bounded the catalog for that batch: one condition, helper arm or set element
dropped, negated or narrowed, at one place it is read. The thirty-second round, run to confirm that bound, still built
687 mutants, and 64 survivors fell within it, every one a narrowing.

## Decision

**Every pull request that changes `src/` runs mutation testing as one finite step of its verification, apart from its
own review. A fixed, first-order catalog (one change per mutant) is run over the code the pull request changes, and
each survivor is settled in the step.**

- **What is mutated.** The lines of `src/` the pull request changes. A test is mutated only when it is an oracle over
  the library's code, a test that reads the library's sources or compiled code and refuses a shape there, such as
  `SeededComparerTests`. Even then, only the conditions the pull request adds to it or changes in it are mutated: the
  reading's conditions, the arms of its helpers, the elements of the sets it reads. A new oracle is thus mutated whole,
  once. Its samples and stand-ins — code written in the test for the reading to refuse or let be, and types imitating
  the library's own —, and every test written to kill a mutant, are never mutated in their turn.
- **The catalog.** Each mutant makes one change, at one place the code writes it:
  1. negate a condition, or drop it: `true` in its place, or `false` where it is one alternative of several;
  2. shift a bound by one: `<` and `<=`, `>` and `>=`, a constant bound `n` and `n ± 1`;
  3. remove a call, or the statement that makes it;
  4. return the default value from a member: `false`, `0`, `null`, an empty collection;
  5. remove one element of a set the code reads (an array, a list of names, an alternative of a pattern), or one arm
     of a switch.

  A helper is mutated once, in its body, and not specialized at each of its callers or along one call path. No mutant
  makes two changes at once. No mutant narrows a condition to a sub-case of it.
- **How a survivor is settled.** A mutant no test fails is a gap in the tests, settled in the step and outside the
  review: it does not count as a review finding and does not restart the review. It ends in one of four ways:
  - a test is written that fails on it;
  - it is classed **equivalent**, with one line saying why no input tells it from the original;
  - in an oracle only, it is classed **widening**, with one line saying what it would refuse that the original lets
    be. Such a mutant only makes the oracle refuse more. If the library ever writes that shape, the widened oracle
    fails on it loudly, where a narrowed one would let it pass in silence;
  - when only an input no test can build tells it apart, a file larger than a test can write for one, it is filed as
    an issue labeled `debt`, under the milestone that will pay it.

  A survivor classed equivalent or widening is never reopened.
- **When the step ends.** After the fixes, the survivors are run again, until each one is settled. Lines of `src/` that
  a later fix changes are lines the pull request changes: they are mutated in their turn, and the lines already
  mutated are not mutated again.

As since batch 5, the pull request's entry in `docs/status.md` gives the number of mutants, how many were killed, and
each survivor classed, filed or left with its reason.

## Consequences

- The step is bounded: the size of the patch times a fixed catalog, then the survivors again until each is settled. It
  ends however many tests its fixes add, because those tests are not mutated.
- The pull request's review counts only what it finds in the library, its tests and its documents. A survivor no
  longer costs a review round.
- Pull requests that never mutated anything, outside the M02 batches, now do. The cost follows the patch: a small
  change has few mutants.
- A first-order catalog misses a defect that shows only when two conditions change together. That is accepted: tests
  that kill every first-order mutant kill nearly all higher-order ones (the coupling effect: A. J. Offutt,
  *Investigations of the software testing coupling effect*, ACM TOSEM 1(1), 1992). A defect along one caller only is
  the review's to find, reading the code paths that mutation no longer walks one by one. Common tools, Stryker.NET and
  PIT among them, also apply each operator once where the source writes it, never per caller.
- Mutation does not search an oracle's open domain. A form of construction the guard test does not read changes no
  condition of it, so no mutant reveals it. Finding it is the review's job, as for any other code.
- An equivalent or a widening is a judgment, and it can be wrong. Its written reason makes it checkable.

### Alternatives rejected

- **Mutating everything the pull request changes, tests included.** Each test written to kill a mutant becomes code to
  mutate, and the step has no fixed point. That is batch 17's regress.
- **Mutating `src/` only, never a test.** It is the shortest step. But nothing would then check the conditions of a
  guard test that reads the library, where one wrong condition lets a whole class of defect through.
- **Mutating every condition of an oracle each time a pull request touches it.** Settled mutants would run again, and
  survivors already classed would be judged again.
- **Batch 17's bound as the rule: each helper specialized at each call site, each condition narrowed.** It builds
  more mutants, and its survivors are ones only the specialization or the narrowing shows. Batch 17's 31st round found
  8 minor findings there and its 32nd 64 survivors, all in the guard test and none in the library.
- **A catalog chosen when each pull request starts, by its risk.** Each pull request would reopen the question this
  record settles.
- **Only the pull requests that change a surface hostile input reaches.** Cheaper, but the rule would then bind code by
  a judgment of where hostile input reaches, which is what the threat model is for, not the tests.
- **A survivor counted as a minor finding, with the catalog fixed.** This converges, but each survivor costs a full
  review round.
- **A widening killed by a sample the oracle must let be.** Strictest, but it spends samples on the direction in which
  a wrong oracle fails loudly anyway.
- **A survivor no test can tell apart classed as equivalent.** The step would end the same, but the gap would be
  written only in a pull request's record, where no later batch looks for work.
- **Survivors past a threshold filed as debt.** This bounds the cost. But it leaves known gaps in a pull request whose
  claim is that its tests hold its defenses.

## What would reopen it

Two things. A defect reaching `main` that no review found and that a mutant outside the catalog would have caught: a
second-order mutant, a helper specialized at one of its callers, or a narrowed condition. Or the step taking a pull
request longer than its review does.
