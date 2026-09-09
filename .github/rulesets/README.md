# Rulesets

The JSON files here are the source of truth for this repository's branch and tag rules. GitHub does not read them on its own, so they are documentation and a reproducible starting point rather than live configuration. Changing a file changes nothing until it is applied.

## Applying them

Either import the file through the web interface, under Settings, Rules, New ruleset, Import a ruleset, or push it with the API:

```sh
gh api --method POST repos/jeffnyman/rezrov/rulesets --input .github/rulesets/protect-main.json
gh api --method POST repos/jeffnyman/rezrov/rulesets --input .github/rulesets/release-tags.json
```

Updating an existing ruleset needs its numeric id and a `PUT` rather than a `POST`:

```sh
gh api repos/jeffnyman/rezrov/rulesets --jq '.[] | "\(.id)  \(.name)"'
gh api --method PUT repos/jeffnyman/rezrov/rulesets/RULESET_ID --input .github/rulesets/protect-main.json
```

If you edit a ruleset through the web interface instead, export it and commit the result so these files do not drift out of date.

## What each one does

`protect-main.json` applies to the default branch. It blocks deletion and force pushes, requires changes to arrive through a pull request, and restricts merging to squash only. Approvals are set to zero, which suits a single maintainer while still routing everything through a pull request. Repository admins can bypass it.

`release-tags.json` applies to `refs/tags/v*`. It blocks deleting or moving a release tag once it exists, with no bypass for anyone.

## Status checks

`protect-main.json` requires one check, `pr-title`, produced by the `pr` workflow. The `integration_id` of 15368 identifies GitHub Actions as the app reporting it.

That check matters more than it looks. Because merges are squashed and the repository is set to take the squash subject from the pull request title, the title is what actually lands in history. The `commit-msg` hook only governs the working commits on a branch, and those get squashed away, so the pull request title is where the Conventional Commits format has to be enforced.

The `build` and `test` contexts come from the `ci` workflow. The test job uses a matrix, and GitHub appends the matrix value to the job key, which is why the contexts read `test (ubuntu-latest)` and so on. Renaming a job in `ci.yml` therefore means updating this file and reapplying the ruleset.

There is no separate formatting check. `Directory.Build.props` sets `EnforceCodeStyleInBuild`, and CI builds with `ContinuousIntegrationBuild=true`, which makes warnings fatal, so an `.editorconfig` violation already fails the `build` context.

The order matters whenever a check is added. A required check that no workflow reports stays pending forever and blocks every pull request, so merge the workflow that produces a context before requiring it.
