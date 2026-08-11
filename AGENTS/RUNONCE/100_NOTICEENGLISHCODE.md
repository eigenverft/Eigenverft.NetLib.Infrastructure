# Notice non-English code and comments

During the run-once phase, inspect repository-owned, human-authored code and comments once for clear use of a language other than English. Do not change anything during this check.

Classify each finding by actual responsibility, not by filename or directory name alone:

- **Product code:** implementation that provides the behavior or interface of a clearly identifiable software product. This includes a declarative profile, manifest, or configuration file when that file is itself the primary product or its entrypoint. Product identifiers and comments should be English.
- **Non-product code:** repository-owned tests, build or release automation, distribution or activation helpers, development tools, examples, and comparable code that supports the product without implementing its behavior or interface. It remains non-product code when it is shipped or executed alongside the product. Report its findings separately as an optional consistency improvement.

Include only clear non-English identifiers and comments for which an accurate English replacement is unambiguous. Treat docstrings, XML documentation comments, comment-based help, and comparable documentation embedded in code as comments. Preserve established domain terms, proper names, externally defined interface names, and quoted external text. Exclude generated, vendored, dependency, archive, and repository-classified local content. Also exclude user-facing localized text, localization resources, test data or fixtures, standalone documentation prose, and other content whose language is intentional or is not code or a code comment.

Do not treat grammar, style, wording quality, or spelling as findings for this instruction. Do not infer or invent a product solely to apply this check. When no concrete product is identifiable, omit the product section but still report qualifying non-product code separately.

When a reported follow-up is later requested, update every affected repository-owned reference with each identifier change. If a replacement or required reference update cannot be resolved unambiguously, skip and report only that item, then continue with the remaining changes.

After all run-once instructions are complete, append the following block to the next user-facing response only when at least one change is recommended. Include only sections that have findings.

```markdown
## EnglishCode

The following English-language changes are recommended for repository-owned code and comments.

Product code

1. In `path/to/file`: `<non-English identifier or comment>` → `<English replacement>`

Non-product code

1. In `path/to/file`: `<non-English identifier or comment>` → `<English replacement>`
```

## Suggested follow-up actions

When this instruction reports at least one finding, contribute one or more suitable suggested follow-up actions to the response. Keep product and non-product changes separately selectable. Use the following as the standard example, adapting, replacing, or extending its actions when the actual findings justify a different or additional follow-up:

```markdown
<letter>) Source: `100_NOTICEENGLISHCODE.md`

1. Apply all reported English-language changes in product code.
2. Apply all reported English-language changes in non-product code.
```
