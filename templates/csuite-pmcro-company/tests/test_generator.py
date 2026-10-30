from __future__ import annotations

import copy
import importlib.util
import json
import tempfile
import unittest
from pathlib import Path


PACKAGE_ROOT = Path(__file__).resolve().parents[1]
GENERATOR_PATH = PACKAGE_ROOT / "tools" / "generate.py"
VALIDATOR_PATH = PACKAGE_ROOT / "tools" / "validate.py"


def load_module(name: str, path: Path):
    spec = importlib.util.spec_from_file_location(name, path)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"cannot load module at {path}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


generator = load_module("company_generator", GENERATOR_PATH)
validator = load_module("company_validator", VALIDATOR_PATH)


class CompanyGeneratorTests(unittest.TestCase):
    def setUp(self) -> None:
        self.config = json.loads((PACKAGE_ROOT / "company.json").read_text(encoding="utf-8"))
        self.temp = tempfile.TemporaryDirectory()
        self.output = Path(self.temp.name) / "company"

    def tearDown(self) -> None:
        self.temp.cleanup()

    def test_generates_ten_executives_thirty_seats_and_nineteen_skills(self) -> None:
        agents, skills = generator.generate(self.config, self.output)
        self.assertEqual((40, 19), (agents, skills))
        self.assertEqual([], validator.validate(self.output))

    def test_rejects_duplicate_role_codes_before_writing(self) -> None:
        config = copy.deepcopy(self.config)
        config["roles"][1]["code"] = config["roles"][0]["code"]
        with self.assertRaisesRegex(ValueError, "duplicate role code"):
            generator.generate(config, self.output)
        self.assertFalse(self.output.exists())

    def test_rejects_undeclared_role_fields(self) -> None:
        config = copy.deepcopy(self.config)
        config["roles"][0]["grant"] = True
        with self.assertRaisesRegex(ValueError, r"roles\[0\] fields"):
            generator.generate(config, self.output)
        self.assertFalse(self.output.exists())

    def test_refuses_to_overwrite_a_nonempty_output_directory(self) -> None:
        self.output.mkdir()
        marker = self.output / "preserve.txt"
        marker.write_text("keep", encoding="utf-8")
        with self.assertRaises(FileExistsError):
            generator.generate(self.config, self.output)
        self.assertEqual("keep", marker.read_text(encoding="utf-8"))

    def test_rejects_path_traversal_company_id(self) -> None:
        config = copy.deepcopy(self.config)
        config["company"]["id"] = "../outside"
        with self.assertRaisesRegex(ValueError, "company.id"):
            generator.generate(config, self.output)

    def test_validator_detects_authority_widening(self) -> None:
        generator.generate(self.config, self.output)
        spec_path = self.output / "agents" / "ceo" / "agent.spec.json"
        spec = json.loads(spec_path.read_text(encoding="utf-8"))
        spec["authority"]["grant"] = True
        spec_path.write_text(json.dumps(spec), encoding="utf-8")
        self.assertTrue(any("authority.grant" in failure for failure in validator.validate(self.output)))


if __name__ == "__main__":
    unittest.main()
