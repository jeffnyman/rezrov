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

## Status checks are deliberately absent

`protect-main.json` has no `required_status_checks` rule yet, because a required check that no workflow ever reports stays pending forever and blocks every pull request permanently. Add the rule once CI exists and has reported the contexts at least once. It looks like this, where `integration_id` 15368 is GitHub Actions:

```json
{
  "type": "required_status_checks",
  "parameters": {
    "strict_required_status_checks_policy": false,
    "do_not_enforce_on_create": false,
    "required_status_checks": [
      { "context": "pr-title", "integration_id": 15368 },
      { "context": "build", "integration_id": 15368 },
      { "context": "test (ubuntu-latest)", "integration_id": 15368 },
      { "context": "test (windows-latest)", "integration_id": 15368 },
      { "context": "test (macos-latest)", "integration_id": 15368 }
    ]
  }
}
```

The `pr-title` check matters more than it looks. Because merges are squashed and the repository is set to take the squash subject from the pull request title, the title is what actually lands in history. The `commit-msg` hook only governs the working commits on a branch, and those get squashed away, so the pull request title is where the Conventional Commits format has to be enforced.
