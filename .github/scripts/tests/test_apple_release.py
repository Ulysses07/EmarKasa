"""Release boundary tests: no Apple account, keychain, network or workloads needed."""
import copy
import datetime as dt
import importlib.util
import os
from pathlib import Path
import unittest
from unittest.mock import patch

SPEC = importlib.util.spec_from_file_location("apple_release", Path(__file__).parents[1] / "apple-release.py")
release = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(release)


class AppleReleaseTests(unittest.TestCase):
    def profile(self):
        return {
            "UUID": "11111111-2222-3333-4444-555555555555",
            "ExpirationDate": dt.datetime(2030, 1, 1),
            "TeamIdentifier": ["TEAM012345"],
            "ApplicationIdentifierPrefix": ["OLDPREFIX0"],
            "Platform": ["iOS"],
            "DeveloperCertificates": [b"public test certificate"],
            "Entitlements": {
                "application-identifier": "OLDPREFIX0.com.royalmezat.kasa",
                "com.apple.developer.team-identifier": "TEAM012345",
                "get-task-allow": False,
            },
        }

    def test_exact_app_store_profile_allows_a_legacy_app_id_prefix(self):
        result = release.validate_profile(self.profile(), "com.royalmezat.kasa")
        self.assertEqual(result["profile_uuid"], self.profile()["UUID"])

    def test_other_bundle_or_expired_or_device_profile_is_rejected(self):
        invalid = []
        for key, value in (("ProvisionedDevices", ["test-device"]), ("ProvisionsAllDevices", True),
                           ("Platform", ["OSX"]), ("ExpirationDate", dt.datetime(2000, 1, 1))):
            profile = self.profile()
            profile[key] = value
            invalid.append(profile)
        profile = self.profile()
        profile["Entitlements"]["application-identifier"] = "OLDPREFIX0.com.other.app"
        invalid.append(profile)
        profile = self.profile()
        profile["Entitlements"]["get-task-allow"] = True
        invalid.append(profile)
        for profile in invalid:
            with self.subTest(profile=profile), self.assertRaises(release.ReleaseError):
                release.validate_profile(profile, "com.royalmezat.kasa")

    def test_build_number_cannot_roll_back_or_be_an_msbuild_expression(self):
        self.assertEqual(release.build_number("8", 8), "8")
        for value in ("", "7", "01", "2147483648", "8;CodesignKey=other", "$(value)"):
            with self.subTest(value=value), self.assertRaises(release.ReleaseError):
                release.build_number(value, 8)

    def test_apple_upload_requires_explicit_confirmation(self):
        env = {name: "do-not-print-this" for name in release.SIGNING}
        env.update(IOS_RELEASE_ACTION="upload-testflight", IOS_BUILD_NUMBER="8", IOS_CONFIRM_UPLOAD="false")
        with patch.dict(os.environ, env, clear=True), patch.object(release, "project_info", return_value={"minimum_build": 8}):
            with self.assertRaisesRegex(release.ReleaseError, "confirm_upload"):
                release.check(Path("."))

    def test_missing_secret_error_reports_names_only(self):
        with patch.dict(os.environ, {"PRESENT": "sensitive-marker"}, clear=True):
            with self.assertRaises(release.ReleaseError) as error:
                release.required(["PRESENT", "ABSENT"])
            self.assertIn("ABSENT", str(error.exception))
            self.assertNotIn("sensitive-marker", str(error.exception))

    def test_ipa_only_build_does_not_require_upload_credentials(self):
        env = {name: "do-not-print-this" for name in release.SIGNING}
        env.update(IOS_RELEASE_ACTION="build-ipa", IOS_BUILD_NUMBER="8")
        with patch.dict(os.environ, env, clear=True), patch.object(release, "project_info", return_value={"minimum_build": 8}):
            release.check(Path("."))


if __name__ == "__main__":
    unittest.main()
