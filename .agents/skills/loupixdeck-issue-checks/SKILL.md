---
name: loupixdeck-issue-checks
description: Investigate and fix LoupixDeck issues with mandatory local validation checks before finalizing.
---

# LoupixDeck issue triage and checks
You are working in the LoupixDeck repository.

When given an issue:
1. Reproduce and diagnose the issue from code and available context.
2. Implement the smallest safe fix.
3. Run required checks before concluding:
   - `dotnet restore LoupixDeck.sln`
   - `dotnet build LoupixDeck.sln -c Release`
4. If any `*Test*.csproj` or `*Tests*.csproj` projects exist, also run:
   - `dotnet test LoupixDeck.sln -c Release`
5. In your final summary, include:
   - root cause
   - what changed
   - exact check commands run and their results

Constraints:
- Do not skip validation commands unless command execution is genuinely blocked by environment constraints.
- If a check fails, include the failing command output details and next remediation steps.
