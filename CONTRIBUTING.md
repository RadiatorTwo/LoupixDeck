# Contributing to LoupixDeck

This document defines the **ground-truth rules** for working on LoupixDeck. The goal is simple:
keep every change small, reviewable, and safe — so that problems are caught in review instead of
shipping.

These rules are **binding by discipline**: there is currently no test suite and no CI gate
enforcing them. Following them is a matter of judgment, not automation. When in doubt, choose the
smaller, more reviewable change.

---

## 1. One change, one concern

A pull request — and ideally each commit within it — should address **exactly one concern**.

**Don't mix these in the same commit:**

- **Behavior changes** (new feature, bug fix, changed logic)
- **Pure refactoring** (rename, extract, move, restructure)
- **File or directory moves**
- **Formatting / whitespace-only churn**

Each is fine on its own. Combined, they produce a diff no reviewer can reason about.

- A move/rename goes in its **own commit** with no content edits, so the diff stays a clean rename.
  If you must move *and* edit a file, move first, commit, then edit.
- A refactoring commit must be **behavior-preserving**. If the code's behavior changes, it's not a
  refactoring — split it out.
- Keep PRs **small enough to review per commit**. Break large work into a sequence of focused PRs,
  each independently buildable.

---

## 2. Architecture & conventions

- Follow the patterns already in the codebase rather than introducing new ones for the same job.
  Match the surrounding code's structure, naming, and idioms.
- **MVVM:** keep logic and state in ViewModels; code-behind is for view concerns only. Use the
  project's established notification mechanism rather than hand-rolling alternatives.
- Converting existing code to a newer pattern is a **refactoring** (see rule 1): it must not change
  behavior and must not share a commit with logic changes.

---

## 3. Code style & formatting

- The repo's `.editorconfig` is the **single source of truth** for style. Consult it rather than
  any prose description; if a rule isn't in it, it isn't enforced.
- Run the formatter before committing and keep the diff clean. If a file needs reformatting, do it
  in a **separate, formatting-only commit** — never fold whitespace churn into a logic change.
- All developer-facing text is **English**: code, comments, identifiers, log strings, and UI.
  This holds regardless of the language of the conversation or issue.

---

## 4. Cross-platform changes

LoupixDeck targets multiple platforms from one codebase.

- When you touch platform-specific code, **handle every supported platform in the same change** —
  never leave one platform broken "to fix later".
- Use the project's established gating mechanism for platform-specific paths; for branches that
  must compile everywhere, use runtime OS checks.
- If you can build/verify only on one platform, **say so explicitly** in the PR description.

---

## 5. Backwards compatibility of user files (MANDATORY)

User-created or -maintained files (settings, macros, config, any persistence file) must **never**
be made unusable by a code change:

- New fields are **optional** with sensible defaults when absent from an old file.
- Fields are **not** renamed, retyped incompatibly, or removed without migrating old values.
- A mandatory format change ships with **automatic migration**, never a silent reset or crash.
- Before committing a data-model/serialization change, load a **pre-change file** and confirm
  identical behavior.

---

## 6. Verify before you commit

With no automated tests, verification is manual and non-negotiable:

- **The build succeeds** — on every platform a change affects.
- **Smoke-test the actual behavior** you changed by running the app, not just the build.
- **Data-model and MVVM changes must be verified end-to-end** — confirm the change works correctly
  both **on the physical device** and in the **GUI**. A property/notification change that looks
  right in code can still fail to propagate to the hardware or the on-screen view (or vice versa),
  so check both surfaces, not just one.
- For serialization/config changes, additionally verify against an **old file** (rule 5).
