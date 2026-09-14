# Security policy

## Why this matters more than usual here

This library parses files that arrive from outside your system. A malformed PDF that makes it crash,
hang, or allocate without bound is not a robustness bug — it is a denial of service in whatever service
embeds it. Those reports are treated as security reports.

So are: reading outside the bounds of the input, following a reference in a file to a resource on the
machine, and anything that lets a document's content influence what the host process does beyond
returning data.

## Supported versions

While the version is below 1.0, only the latest published version receives fixes. After 1.0, the latest
major does.

## Reporting a vulnerability

Please **do not** open a public issue.

Use GitHub's private vulnerability reporting: the **Security** tab of this repository → **Report a
vulnerability**. That opens an advisory visible only to the maintainer, and is the fastest way to get a
fix moving without disclosing the issue before a patch exists.

If a document reproduces the problem, attach it — a file that triggers the bug is worth more than any
description of it. If the document is confidential, say so and send a reduced or synthetic reproduction
instead; the reporting thread is private, but a published advisory and its regression test are not.

You should get an initial response within a few days. A confirmed report is fixed privately, and a
GitHub Security Advisory is published alongside the patched release, crediting the reporter unless they
prefer otherwise.
