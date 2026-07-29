# Public Repo Privacy

Hearth Calendar is intended to be a public repository, so planning docs, examples, tests, sample config, and issue descriptions should avoid real personal details.

## Rules

- Do not use real household member names in docs, examples, tests, seed data, or screenshots.
- Use neutral placeholders such as `Adult A`, `Adult B`, and `Child`.
- Use neutral IDs such as `adult-a`, `adult-b`, and `child`.
- Do not include real addresses, school names, clubs, workplaces, phone numbers, email addresses, calendar URLs, tokens, or internal hostnames.
- Do not commit real calendar event data.
- Do not commit real Home Assistant, feed, CalDAV, AI provider, or admin credentials.
- Use sample dates only when they are generic examples.
- Keep public docs focused on product behaviour and architecture, not private household context.

## Examples

Use:

```text
Adult B birthday on 25 July
Child swimming with Adult B
Dentist for Adult A
```

Avoid:

```text
<real name> birthday
<real child name> swimming at <real venue>
<real workplace> calendar feed
```

## Acceptance Criteria

- `rg` for known private names returns no matches outside files where legal attribution is intentional.
- Sample config contains placeholders only.
- Tests use anonymised people and generic events.
- Screenshots or generated assets do not reveal live household data.
- Plans and task breakdowns use neutral role labels.
