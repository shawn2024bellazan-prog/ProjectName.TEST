#!/usr/bin/env python3
"""Generate a draft C-suite PMCR-O company from a JSON declaration."""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path
from typing import Any


ID_PATTERN = re.compile(r"^[a-z0-9]+(-[a-z0-9]+)*$")
VERSION_PATTERN = re.compile(r"^[0-9]+\.[0-9]+\.[0-9]+$")
PHASES = (
    ("planner", "PLAN", "Plan"),
    ("maker", "MAKE", "Make"),
    ("checker", "CHECK", "Check"),
)
PMCR_O = ["ORCHESTRATE", "PLAN", "MAKE", "CHECK", "REFLECT"]

CORE_SKILLS = [
    ("orchestrate", "Orchestrate", "Accept or reject an intent and route a bounded PMCR-O cycle."),
    ("plan", "Plan", "Produce the smallest evidence-bound plan for an accepted intent."),
    ("make", "Make", "Make one accepted plan inside its declared target."),
    ("check", "Check", "Compare an artifact to acceptance criteria without self-approval."),
    ("reflect", "Reflect", "Record one earned constraint and one unexecuted next seed."),
    ("delegation", "Delegate", "Propose bounded specialist work without widening authority."),
    ("evidence-intake", "Prepare evidence", "Record provenance and limits for caller-supplied evidence."),
    ("identity-trust", "Review identity and trust", "Check declared identities and delegation ceilings."),
]
GOVERNANCE_SKILLS = [
    ("authority-boundaries", "Respect authority boundaries", "Keep discovery, registration, grants, activation, and execution separate."),
]


def load_config(path: Path) -> dict[str, Any]:
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        raise ValueError(f"cannot read company configuration: {error}") from error
    validate_config(value)
    return value


def validate_config(value: Any) -> None:
    if not isinstance(value, dict) or value.get("schema") != "projectname.csuite-company/v1":
        raise ValueError("schema must be projectname.csuite-company/v1")
    if set(value) != {"schema", "company", "roles"}:
        raise ValueError("top-level fields must be schema, company, and roles")
    company = value.get("company")
    roles = value.get("roles")
    if not isinstance(company, dict) or not isinstance(roles, list) or not roles:
        raise ValueError("company object and non-empty roles array are required")
    if set(company) != {"id", "name", "version", "description"}:
        raise ValueError("company fields must be id, name, version, and description")
    for field in ("id", "name", "version", "description"):
        if not isinstance(company.get(field), str) or not company[field].strip():
            raise ValueError(f"company.{field} is required")
    if not ID_PATTERN.fullmatch(company["id"]):
        raise ValueError("company.id must be a lowercase kebab-case identifier")
    if not VERSION_PATTERN.fullmatch(company["version"]):
        raise ValueError("company.version must use major.minor.patch")

    seen: set[str] = set()
    for index, role in enumerate(roles):
        if not isinstance(role, dict):
            raise ValueError(f"roles[{index}] must be an object")
        if set(role) != {"code", "title", "domain", "mission", "canOrchestrate"}:
            raise ValueError(
                f"roles[{index}] fields must be code, title, domain, mission, and canOrchestrate"
            )
        for field in ("code", "title", "domain", "mission"):
            if not isinstance(role.get(field), str) or not role[field].strip():
                raise ValueError(f"roles[{index}].{field} is required")
        if not re.fullmatch(r"[A-Z][A-Z0-9]*", role["code"]):
            raise ValueError(f"roles[{index}].code must be uppercase letters and digits")
        if role["code"] in seen:
            raise ValueError(f"duplicate role code: {role['code']}")
        seen.add(role["code"])
        if not ID_PATTERN.fullmatch(role["domain"]):
            raise ValueError(f"roles[{index}].domain must be a lowercase kebab-case identifier")
        if not isinstance(role.get("canOrchestrate"), bool):
            raise ValueError(f"roles[{index}].canOrchestrate must be boolean")


def skill_catalog(roles: list[dict[str, Any]]) -> list[dict[str, str]]:
    entries = [
        {"id": f"pmcro-{key}", "name": name, "description": description}
        for key, name, description in CORE_SKILLS
    ]
    entries.extend(
        {"id": f"governance-{key}", "name": name, "description": description}
        for key, name, description in GOVERNANCE_SKILLS
    )
    entries.extend(
        {
            "id": f"{role['code'].lower()}-domain",
            "name": f"{role['title']} domain",
            "description": role["mission"],
        }
        for role in roles
    )
    return entries


def _write_json(path: Path, value: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def _write_text(path: Path, text: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text.rstrip() + "\n", encoding="utf-8")


def _yaml_string(value: str) -> str:
    return json.dumps(value, ensure_ascii=False)


def _agent_id(role: dict[str, Any], seat: str | None = None) -> str:
    role_id = role["code"].lower()
    return f"{role_id}-{seat}-seat" if seat else role_id


def _card_skill(agent_id: str, title: str, description: str) -> dict[str, Any]:
    return {
        "id": f"{agent_id}.inspect",
        "name": f"{title} review",
        "description": description,
        "tags": ["pmcro", "recommendation-only"],
        "examples": [f"Review a supplied {title.lower()} intent and return evidence-bound recommendations."],
    }


def _agent_files(root: Path, company: dict[str, Any], role: dict[str, Any],
                 agent_id: str, title: str, responsibility: str, phase: str,
                 skills: list[dict[str, Any]]) -> None:
    agent_dir = root / "agents" / agent_id
    next_phase = {"PLAN": "make", "MAKE": "check", "CHECK": "reflect"}.get(phase, "plan")
    next_seed = (
        f'@{agent_id} /pmcro:{next_phase} "Continue one owner-authorized {role["domain"]} '
        'intent; return the minimum evidence-bound plan, one earned constraint, '
        'and one UNEXECUTED next seed. Do not claim Checker PASS or execute."'
    )
    spec = {
        "schema": "projectname.company-agent/v1",
        "id": agent_id,
        "name": title,
        "state": "DRAFT_UNREGISTERED",
        "company": company["id"],
        "executiveRole": role["code"],
        "domain": role["domain"],
        "responsibility": responsibility,
        "phase": phase,
        "pmcro": PMCR_O,
        "authority": {
            "defaultAdmission": "DENY",
            "registration": False,
            "grant": False,
            "activation": False,
            "execution": False,
            "tools": [],
            "mcpGrants": [],
        },
        "checker": {
            "independentPrincipalRequired": True,
            "evidenceDigestBindingRequired": True,
            "verdict": "NOT_CLAIMED",
        },
        "a2a": {
            "card": "agent-card.json",
            "protocolVersion": "1.0",
            "mounted": False,
        },
        "earnedConstraint": "This draft can recommend only; implementation and approval require separately authorized humans and independent checks.",
        "nextSeed": next_seed,
        "nextSeedExecuted": False,
    }
    card = {
        "name": title,
        "description": f"Draft {title}; recommendation only, with no tools or grants.",
        "version": company["version"],
        "supportedInterfaces": [
            {
                "url": f"https://agents.invalid/{company['id']}/{agent_id}/a2a",
                "protocolBinding": "JSONRPC",
                "protocolVersion": "1.0",
            }
        ],
        "capabilities": {
            "streaming": False,
            "pushNotifications": False,
            "extendedAgentCard": False,
        },
        "defaultInputModes": ["text/plain"],
        "defaultOutputModes": ["text/plain", "application/json"],
        "skills": skills,
    }
    _write_json(agent_dir / "agent.spec.json", spec)
    _write_json(agent_dir / "agent-card.json", card)
    _write_text(
        agent_dir / f"{agent_id}.agent.md",
        f"""---
name: {agent_id}
description: "Draft {title}; recommendation only, no tools or grants."
state: DRAFT_UNREGISTERED
---

I am the {title} for {company["name"]}.

My responsibility is to {responsibility[0].lower() + responsibility[1:]}
My domain is {role["domain"]}. I follow ORCHESTRATE, PLAN, MAKE, CHECK, REFLECT
as a bounded workflow and hand off evidence rather than advancing phases by
message alone.

I do not call models, invoke tools, make business decisions, or perform actions.
My Checker role is not an authenticated independent Checker PASS. Discovery of
my placeholder A2A card is not registration, a grant, activation, or execution.

My earned constraint: {spec["earnedConstraint"]}

UNEXECUTED next seed: {next_seed}
""",
    )


def _generate_skills(root: Path, company: dict[str, Any],
                     skills: list[dict[str, str]]) -> None:
    for skill in skills:
        skill_root = root / "skills" / skill["id"]
        _write_text(
            skill_root / "SKILL.md",
            f"""---
name: {skill["id"]}
description: {_yaml_string(skill["description"] + " Use only for draft, recommendation-only PMCR-O work.")}
metadata:
  state: DRAFT_UNREGISTERED
---

# {skill["name"]}

{skill["description"]}

## Procedure

1. Read `references/guide.md` and the caller-supplied evidence.
2. Stop if authority, ownership, required evidence, or acceptance criteria are missing.
3. Use `assets/output.template.md`; make no claim beyond the supplied evidence.
4. Hand off for an independent, authenticated Checker bound to the artifact digest.
5. Leave one earned constraint and exactly one unexecuted next seed.

## Limits

This skill has no tools and grants no authority. It does not activate or execute
agents, call models, publish content, or perform business actions for {company["name"]}.
""",
        )
        _write_text(
            skill_root / "references" / "guide.md",
            f"""# {skill["name"]} guide

Use this skill only for the declared purpose: {skill["description"]}

Accept evidence only from the caller or an explicitly authorized source. Record
what was supplied, what was observed, and what remains unknown. Reject requests
to imply access, identity, approval, or execution that has not been verified.

This guide is a draft process description, not an authorization or a runtime
implementation. A structural check is not an independent Checker verdict.
""",
        )
        _write_text(
            skill_root / "assets" / "output.template.md",
            f"""# {skill["name"]} output

## Intent

## Supplied evidence and provenance

## Findings

## Unknowns and authority limits

## Acceptance criteria

## Earned constraint

## UNEXECUTED next seed
""",
        )


def generate(config: dict[str, Any], output: Path) -> tuple[int, int]:
    validate_config(config)
    if output.exists():
        if not output.is_dir() or any(output.iterdir()):
            raise FileExistsError(f"output must be a new or empty directory: {output}")
    output.mkdir(parents=True, exist_ok=True)
    company = config["company"]
    roles = config["roles"]
    skills = skill_catalog(roles)
    agent_entries: list[dict[str, str]] = []

    for role in roles:
        chief_id = _agent_id(role)
        chief_skills = [
            _card_skill(chief_id, role["title"], role["mission"]),
            _card_skill(chief_id, "PMCR-O", "Coordinate only a bounded, advisory PMCR-O workflow."),
        ]
        _agent_files(
            output,
            company,
            role,
            chief_id,
            role["title"],
            role["mission"],
            "ORCHESTRATE" if role["canOrchestrate"] else "ADVISE",
            chief_skills,
        )
        agent_entries.append(
            {"id": chief_id, "name": role["title"], "kind": "executive", "path": f"agents/{chief_id}"}
        )
        for seat_id, phase, label in PHASES:
            agent_id = _agent_id(role, seat_id)
            title = f"{role['code']} {label} Seat"
            responsibility = (
                f"prepare the {phase} phase for {role['domain']} work under the "
                "executive's declared scope"
            )
            seat_skill = _card_skill(agent_id, f"{label} seat", responsibility)
            _agent_files(
                output,
                company,
                role,
                agent_id,
                title,
                responsibility,
                phase,
                [seat_skill],
            )
            agent_entries.append(
                {"id": agent_id, "name": title, "kind": f"{seat_id}-seat", "path": f"agents/{agent_id}"}
            )

    _generate_skills(output, company, skills)
    _write_json(output / "company.json", config)
    _write_json(
        output / "marketplace.json",
        {
            "schema": "projectname.marketplace/v1",
            "name": company["id"],
            "version": company["version"],
            "state": "DRAFT_UNREGISTERED",
            "registration": False,
            "grant": False,
            "activation": False,
            "executionAllowed": False,
            "a2aMounted": False,
            "agentCount": len(agent_entries),
            "skillCount": len(skills),
            "agents": agent_entries,
            "skills": [{"id": skill["id"], "name": skill["name"]} for skill in skills],
        },
    )
    _write_text(
        output / "README.md",
        f"""# {company["name"]}

{company["description"]}

This generated company is a DRAFT_UNREGISTERED declarative example. Its
{len(agent_entries)} agents and {len(skills)} skills have no tools, grants,
runtime, model access, mounted A2A routes, or execution authority. Review
`company.json`, `marketplace.json`, and each `agent.spec.json` before any
separately governed lifecycle decision.
""",
    )
    return len(agent_entries), len(skills)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--config", type=Path, required=True, help="company JSON declaration")
    parser.add_argument("--output", type=Path, required=True, help="new or empty output directory")
    args = parser.parse_args(argv)
    try:
        counts = generate(load_config(args.config), args.output)
    except (ValueError, FileExistsError) as error:
        print(f"REJECT: {error}", file=sys.stderr)
        return 2
    print(f"Generated {counts[0]} draft agents and {counts[1]} draft skills.")
    print("No tools, grants, activations, execution, or A2A routes were created.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
