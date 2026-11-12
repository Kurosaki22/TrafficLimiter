# Workflow

- Never push to `main`. Work on a branch and merge through a pull request; only **squash merge** is allowed.
- Every merged PR is a new release: bump `<Version>` in `TrafficLimiter.App/TrafficLimiter.App.csproj` in that PR
  (semver). `.github/workflows/release.yml` builds and publishes the GitHub release `v<Version>` after the merge
  and fails if that tag already exists.
- The version is shown top-left in the window (read from the assembly), so it needs no second edit.
- README screenshots: `tools/capture-screenshots.ps1` (elevated; backs up and restores settings).
