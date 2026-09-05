# Story folders

One folder per user story. Its name is what `folder:` points at in `BACKLOG.yaml`.

    <story-slug>/
      SKILL.md          what the story is — description and acceptance criteria (prose)
      tasks.yaml        what has to be built, and whether it is done
      test-cases.yaml   what has to be verified, and whether it passed

`BACKLOG.yaml` is the index: epics, stories, status. It never holds tasks or test cases, so the
board draws without opening a single one of these folders.

## tasks.yaml

```yaml
- text: Build the API
  done: true
- text: Build the page
  done: false
```

## test-cases.yaml

`status` is one of `Not Run`, `Passed`, `Failed`.

```yaml
- text: A user cannot read another user's record
  status: Passed
```

## SKILL.md

Prose, written for whoever picks the story up. Its `## Tasks` section is the refinement narrative —
one deliverable per line, each tagged with the acceptance criteria it satisfies. That is a different
thing from `tasks.yaml`, which is the short tickable checklist the board counts.

Acceptance criteria live here as `- [ ]` items. The ones you actually execute become entries in
`test-cases.yaml`.

## Editing by hand

AI Friendly Task Manager writes these files, but they are plain text and yours to edit. If the YAML is malformed
the app names the file and the line rather than guessing — press **Sync** to check the whole backlog
at once, including stories pointing at folders that are not there.
