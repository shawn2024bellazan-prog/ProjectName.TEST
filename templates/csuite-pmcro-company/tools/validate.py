#!/usr/bin/env python3
"""Validate a generated company without claiming an independent Checker verdict."""

from __future__ import annotations

import json
import sys
from pathlib import Path
from typing import Any


def _read_json(path: Path) -> Any:
    return json.loads(path.read_text(encoding="utf-8"))


def validate(root: Path) -> list[str]:
    failures: list[str] = []
    try:
        marketplace = _read_json(root / "marketplace.json")
        company = _read_json(root / "company.json")
    except (OSError, json.JSONDecodeError) as error:
        return [f"cannot read generated marketplace or company declaration: {error}"]

    for flag in ("registration", "grant", "activation", "executionAllowed", "a2aMounted"):
        if marketplace.get(flag) is not False:
            failures.append(f"marketplace.{flag} must be false")
    if marketplace.get("state") != "DRAFT_UNREGISTERED":
        failures.append("marketplace.state must be DRAFT_UNREGISTERED")
    if marketplace.get("agentCount") != len(marketplace.get("agents", [])):
        failures.append("agentCount does not match the catalog")

    agent_ids: set[str] = set()
    for entry in marketplace.get("agents", []):
        agent_id = entry.get("id", "")
        if agent_id in agent_ids:
            failures.append(f"duplicate catalog agent id: {agent_id}")
        agent_ids.add(agent_id)
        if entry.get("path") != f"agents/{agent_id}":
            failures.append(f"{agent_id}: catalog path must be agents/{agent_id}")
            continue
        directory = root / entry.get("path", "")
        try:
            spec = _read_json(directory / "agent.spec.json")
            card = _read_json(directory / "agent-card.json")
            agent_markdown = (directory / f"{agent_id}.agent.md").read_text(encoding="utf-8")
        except (OSError, json.JSONDecodeError) as error:
            failures.append(f"{agent_id}: missing or invalid generated file: {error}")
            continue
        if spec.get("state") != "DRAFT_UNREGISTERED":
            failures.append(f"{agent_id}: state must be DRAFT_UNREGISTERED")
        authority = spec.get("authority", {})
        for flag in ("registration", "grant", "activation", "execution"):
            if authority.get(flag) is not False:
                failures.append(f"{agent_id}: authority.{flag} must be false")
        if authority.get("defaultAdmission") != "DENY":
            failures.append(f"{agent_id}: default admission must be DENY")
        if authority.get("tools") or authority.get("mcpGrants"):
            failures.append(f"{agent_id}: tools and grants must be empty")
        if spec.get("checker", {}).get("verdict") != "NOT_CLAIMED":
            failures.append(f"{agent_id}: must not claim a Checker verdict")
        if spec.get("checker", {}).get("independentPrincipalRequired") is not True:
            failures.append(f"{agent_id}: independent Checker requirement missing")
        if spec.get("a2a", {}).get("mounted") is not False:
            failures.append(f"{agent_id}: A2A must not be mounted")
        if spec.get("nextSeedExecuted") is not False or not spec.get("nextSeed"):
            failures.append(f"{agent_id}: must retain one unexecuted next seed")
        interfaces = card.get("supportedInterfaces", [])
        if len(interfaces) != 1 or not interfaces[0].get("url", "").startswith("https://agents.invalid/"):
            failures.append(f"{agent_id}: A2A URL must be a .invalid placeholder")
        elif (
            interfaces[0].get("protocolBinding") != "JSONRPC"
            or interfaces[0].get("protocolVersion") != "1.0"
        ):
            failures.append(f"{agent_id}: A2A binding/version must be JSONRPC/1.0")
        if card.get("capabilities") != {
            "streaming": False,
            "pushNotifications": False,
            "extendedAgentCard": False,
        }:
            failures.append(f"{agent_id}: A2A capabilities must be disabled")
        if "DRAFT_UNREGISTERED" not in agent_markdown:
            failures.append(f"{agent_id}: markdown must identify draft state")
        if "{{" in agent_markdown:
            failures.append(f"{agent_id}: unfilled template token")

    skill_ids: set[str] = set()
    for skill in marketplace.get("skills", []):
        skill_id = skill.get("id", "")
        if skill_id in skill_ids:
            failures.append(f"duplicate catalog skill id: {skill_id}")
        skill_ids.add(skill_id)
        skill_root = root / "skills" / skill_id
        for relative in (
            Path("SKILL.md"),
            Path("references") / "guide.md",
            Path("assets") / "output.template.md",
        ):
            if not (skill_root / relative).is_file():
                failures.append(f"{skill_id}: missing {relative.as_posix()}")
    if marketplace.get("skillCount") != len(skill_ids):
        failures.append("skillCount does not match the catalog")
    if company.get("company", {}).get("id") != marketplace.get("name"):
        failures.append("company identity does not match marketplace name")
    return failures


def main(argv: list[str] | None = None) -> int:
    if len(argv if argv is not None else sys.argv[1:]) != 1:
        print("usage: validate.py <generated-company-directory>", file=sys.stderr)
        return 2
    root = Path((argv if argv is not None else sys.argv[1:])[0])
    failures = validate(root)
    print(f"failures: {len(failures)}")
    for failure in failures:
        print(f"FAIL: {failure}")
    print("This structural validation is not an independent Checker PASS.")
    return 1 if failures else 0


if __name__ == "__main__":
    raise SystemExit(main())
