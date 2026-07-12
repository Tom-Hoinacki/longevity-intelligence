# Agent Instructions

Mission:
Build the structured intelligence layer for longevity.

Core Rule:
Every claim must be backed by evidence and sources.

Do Not:
- Give medical advice
- Make unsupported health claims
- Mix sponsorship with evidence scores
- Store user health data in the public evidence database
- Change schema without a migration

Workflow:
1. Create or update migrations
2. Add or update seed data
3. Validate data
4. Update documentation
5. Explain changes before committing

Core Data Model:
Asset → Claim → Source → Evidence

Cloud development rules:
- Migration files are the schema source of truth.
- Do not edit schema directly in the dashboard unless the change is also captured in a migration.
- Always run a dry run before deployment and require human approval before cloud changes.
- Never select or connect to a production project automatically.
- Never use `supabase db reset --linked`.
- Never expose or store secrets in the repository.
- Never store personal health data in the public educational schema.

Repository skills:
- Repository-scoped skills live in `.agents/skills` and should be used when a task matches their descriptions.
- Project-specific safety rules in this file override generic external advice.
- Never execute newly downloaded skill scripts without reviewing them first.

GitHub publication:
- After resolving `$GhExe`, always test authentication with `& $GhExe auth status --hostname github.com`.
- If authentication is valid, continue the goal without involving the user.
- If authentication is invalid, expired, missing, or rejected, do not declare the goal blocked immediately. Tell the user that GitHub authentication needs renewal and that a browser login is being launched, then run:
  ```powershell
  & $GhExe auth login `
      --hostname github.com `
      --git-protocol https `
      --web `
      --clipboard
  ```
- This must launch the GitHub device-login flow, display the one-time code, copy it to the Windows clipboard when supported, and open the GitHub authentication page. Tell the user: `GitHub authentication needs renewal. I launched the GitHub device-login page and copied the one-time code to your clipboard. Paste the code into the browser and approve access, then tell me when it is complete.`
- After login completes, automatically verify authentication:
  ```powershell
  & $GhExe auth status --hostname github.com

  if ($LASTEXITCODE -ne 0) {
      throw "GitHub authentication is still invalid after the browser login attempt."
  }
  ```
- Then resume the interrupted push, PR, CI, readiness, or merge operation. If the browser does not open automatically but a device code was produced or copied, try `Start-Process "https://github.com/login/device"`.
- Ask the user only to paste or confirm the one-time code and approve GitHub access. Never ask for a GitHub token in chat, print/log/commit/expose tokens, use `--with-token` unless the user explicitly chooses personal-token authentication, or store tokens in repository files. An expired token is not an implementation failure. The agent must attempt browser/device reauthentication before abandoning an otherwise completed goal; the user must approve OAuth access.
