"""Run with `python -m unittest discover -s tools -p 'test_*.py'`.

Only synthetic temporary files are used; no game assets are required.
"""
import hashlib
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

from bundle_reader import DONOR_SHA256, safe_output, verify_donor


class ExportSafetyTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.bundle = self.root / "donor.bundle"
        self.bundle.write_bytes(b"synthetic test bytes; not a game asset")
        self.original = self.bundle.read_bytes()

    def tearDown(self):
        self.assertEqual(self.original, self.bundle.read_bytes())

    def test_expected_donor_contract(self):
        self.assertEqual(
            DONOR_SHA256,
            "F3948D1FB4252D2A756F1A5249D7220A500B2510955CEB77E0C89CC8096245C1",
        )

    def test_unknown_donor_rejected(self):
        with self.assertRaisesRegex(ValueError, "Unsupported donor SHA256"):
            verify_donor(self.bundle)

    def test_digest_calculation_accepts_only_matching_bytes(self):
        # Mock only the test contract; the CLI has no override option.
        digest = hashlib.sha256(self.original).hexdigest().upper()
        with patch("bundle_reader.DONOR_SHA256", digest):
            self.assertEqual(verify_donor(self.bundle), self.bundle.resolve())

    def test_donor_is_not_a_directory(self):
        with self.assertRaises(ValueError):
            verify_donor(self.root)

    def test_output_cannot_equal_bundle(self):
        for directory in (True, False):
            with self.subTest(directory=directory), self.assertRaises(ValueError):
                safe_output(self.bundle, self.bundle, directory=directory)

    def test_output_cannot_contain_bundle(self):
        with self.assertRaises(ValueError):
            safe_output(self.bundle, self.root, directory=True)

    def test_path_alias_cannot_overwrite_bundle(self):
        alias = self.root / "irrelevant" / ".." / "donor.bundle"
        with self.assertRaises(ValueError):
            safe_output(self.bundle, alias, directory=False)

    def test_hardlink_cannot_overwrite_bundle(self):
        alias = self.root / "hardlink.json"
        try:
            alias.hardlink_to(self.bundle)
        except OSError as error:
            self.skipTest("Hardlinks not supported: " + str(error))
        with self.assertRaises(ValueError):
            safe_output(self.bundle, alias, directory=False)

    def test_new_pose_file_allowed(self):
        target = self.root / "new-poses.json"
        self.assertEqual(safe_output(self.bundle, target, directory=False), target.resolve())
        self.assertFalse(target.exists())

    def test_existing_pose_file_rejected(self):
        target = self.root / "poses.json"
        target.write_text("{}", encoding="utf8")
        with self.assertRaises(ValueError):
            safe_output(self.bundle, target, directory=False)
        self.assertEqual(target.read_text(encoding="utf8"), "{}")

    def test_new_and_empty_payload_directories_allowed(self):
        target = self.root / "payload"
        self.assertEqual(safe_output(self.bundle, target, directory=True), target.resolve())
        self.assertFalse(target.exists())
        target.mkdir()
        self.assertEqual(safe_output(self.bundle, target, directory=True), target.resolve())

    def test_nonempty_payload_directory_rejected(self):
        target = self.root / "payload"
        target.mkdir()
        (target / "scene.json").write_text("{}", encoding="utf8")
        with self.assertRaises(ValueError):
            safe_output(self.bundle, target, directory=True)


if __name__ == "__main__":
    unittest.main()
