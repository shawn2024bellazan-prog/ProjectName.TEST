# C-suite PMCR-O Agent Company Template

This package generates a declarative example company with ten executive agents,
three PMCR-O seats per executive, and nineteen reusable skills. The source
configuration is `company.json`; the generated example is in `example-company/`.

## Generate and validate

Requires Python 3.10+ and uses only the standard library:

```powershell
py -3.12 tools\generate.py --config company.json --output generated-company
py -3.12 tools\validate.py generated-company
py -3.12 -m unittest discover -s tests -v
```

On other platforms, use `python3` (3.10 or later) in place of `py -3.12`.

The generator refuses a non-empty output directory and does not delete files.
Edit `company.json` to define another company. The JSON Schema is provided at
`schema/company.schema.json`.

## Authority boundary

This is a draft artifact template, not an autonomous runtime. Generated agents
have no tools or grants, default admission is DENY, and all registration,
activation, execution, and A2A mounting flags are false. A2A URLs use the
reserved `.invalid` domain. Checker seats can prepare a review, but cannot
produce an authenticated independent Checker PASS. No model is called and no
business action is performed by generation or validation.

The package records source versions checked on 2026-10-29 in
`standards.lock.json`. These are reference pins, not proof of production
readiness. Re-check upstream sources before implementing a runtime adapter.

## Included package files

- `company.json`: editable declarative company input
- `schema/`: input contract
- `tools/`: generator and output validator
- `tests/`: standard-library regression tests
- `standards.lock.json`: dated upstream version/source snapshot
- `example-company/`: generated draft output
