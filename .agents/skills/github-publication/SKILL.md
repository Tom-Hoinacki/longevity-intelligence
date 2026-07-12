# GitHub publication

Use this skill for GitHub push, pull request, CI, readiness, and merge workflows.

## Authentication recovery

After resolving `$GhExe`, always test authentication:

```powershell
& $GhExe auth status --hostname github.com
```

If authentication is valid, continue without involving the user. If it is invalid, expired, missing, or rejected, do not declare the goal blocked immediately. Tell the user clearly that GitHub authentication needs renewal and that a browser login is being launched, then launch the browser/device flow yourself:

```powershell
& $GhExe auth login `
    --hostname github.com `
    --git-protocol https `
    --web `
    --clipboard
```

The command should display the one-time device code, copy it to the Windows clipboard when supported, and open the GitHub authentication page. Tell the user:

> GitHub authentication needs renewal. I launched the GitHub device-login page and copied the one-time code to your clipboard. Paste the code into the browser and approve access, then tell me when it is complete.

After the login command completes, automatically verify authentication:

```powershell
& $GhExe auth status --hostname github.com

if ($LASTEXITCODE -ne 0) {
    throw "GitHub authentication is still invalid after the browser login attempt."
}
```

Then resume the interrupted push, PR, CI, readiness, or merge operation. If the browser does not open automatically but the device code was produced or copied, try:

```powershell
Start-Process "https://github.com/login/device"
```

Ask the user only to paste or confirm the one-time code and approve GitHub access. Never ask the user to paste a GitHub token into chat; print, log, commit, or expose an authentication token; use `--with-token` unless the user explicitly chooses personal-token authentication; or store tokens in repository files. Treat an expired token as an authentication-recovery event, not an implementation failure. The agent must attempt browser/device reauthentication before abandoning an otherwise completed goal. The agent cannot approve GitHub OAuth access on the user’s behalf.
