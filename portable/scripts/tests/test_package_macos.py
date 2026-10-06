"""发布材料回归：实际分发筛选、原文许可、重名材料和完整Mach-O指纹。"""
import importlib.util
import json
from pathlib import Path
import plistlib
import tempfile
import unittest
from unittest.mock import patch

SPEC = importlib.util.spec_from_file_location("package_macos", Path(__file__).resolve().parents[1] / "package_macos.py")
package = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(package)


class PackageTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="neeview-package-tests-")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)

    def make_package(self, name, license_type="expression", license_value="MIT"):
        source = self.root / "packages" / name
        source.mkdir(parents=True)
        (source / f"{name}.nuspec").write_text(f'<package><metadata><license type="{license_type}">{license_value}</license><authors>Actual authors</authors></metadata></package>')
        return source

    def test_duplicate_license_names_keep_both_original_directories_and_third_party(self):
        source = self.make_package("native")
        for relative, text in [("LICENSE.txt", "wrapper"), ("codec/LICENSE.txt", "codec"), ("THIRD-PARTY-NOTICES.txt", "native notices")]:
            path = source / relative; path.parent.mkdir(parents=True, exist_ok=True); path.write_text(text)
        destination = self.root / "Resources/licenses"
        record = package.package_record("Native", "1", source, destination)
        self.assertEqual(3, len(record["license_files"]))
        for material in record["license_files"]:
            original = source / material["source"]
            result = self.root / "Resources" / material["file"]
            self.assertEqual(original.read_bytes(), result.read_bytes())
            self.assertEqual(package.sha256(original), material["sha256"])

    def test_missing_declared_license_fails_instead_of_generating_success(self):
        source = self.make_package("missing", "file", "COPYING")
        with self.assertRaisesRegex(RuntimeError, "许可文件缺失"):
            package.package_record("Missing", "1", source, self.root / "Resources/licenses")

    def test_nonstandard_declared_license_is_copied_and_metadata_only_is_explicit(self):
        source = self.make_package("custom", "file", "COPYING")
        (source / "COPYING").write_text("actual custom terms")
        record = package.package_record("Custom", "1", source, self.root / "Resources/licenses")
        self.assertEqual("included", record["license_material_state"])
        self.assertEqual("COPYING", record["license_files"][0]["source"])
        source = self.make_package("expression")
        record = package.package_record("Expression", "1", source, self.root / "Resources/licenses")
        self.assertEqual("metadata_only", record["license_material_state"])

    def test_declared_license_cannot_read_outside_the_package(self):
        source = self.make_package("escape", "file", "../secret")
        (source.parent / "secret").write_text("private unrelated material")
        with self.assertRaises(RuntimeError):
            package.package_record("Escape", "1", source, self.root / "Resources/licenses")
        self.assertFalse((self.root / "Resources/licenses/escape-1/secret").exists())

    def test_macho_inventory_includes_extensionless_host_fat_framework_and_symlink_once(self):
        app = self.root / "NeeView.app"
        main = app / "Contents/MacOS/NeeView"; main.parent.mkdir(parents=True); main.write_bytes(b"\xcf\xfa\xed\xfehost")
        library = app / "Contents/Frameworks/Example.framework/Versions/A/Example"; library.parent.mkdir(parents=True); library.write_bytes(b"\xca\xfe\xba\xbffat64")
        (library.parents[2] / "Example").symlink_to("Versions/A/Example")
        (main.parent / "managed.dll").write_bytes(b"MZmanaged")
        self.assertEqual({main, library}, set(package.native_binaries(app)))

    def test_resolved_graph_is_filtered_against_real_bundle_and_runtime_pack_is_attributed(self):
        root = self.root / "repo/portable"
        assets_path = root / "src/NeeView.MacOS/obj/project.assets.json"; assets_path.parent.mkdir(parents=True)
        included = self.make_package("included"); (included / "LICENSE").write_text("included copyright")
        omitted = self.make_package("buildonly")
        runtime = self.make_package("runtime"); (runtime / "LICENSE").write_text("runtime copyright")
        assets_path.write_text(json.dumps({"targets": {"net10.0-macos/osx-arm64": {
            "Included/1": {"type": "package", "runtime": {"lib/net10.0/Included.dll": {}}},
            "BuildOnly/1": {"type": "package", "runtime": {"lib/net10.0/NotShipped.dll": {}}}}},
            "libraries": {"Included/1": {"path": "included"}, "BuildOnly/1": {"path": "buildonly"}},
            "packageFolders": {str(included.parent): {}}}))
        resources = self.root / "NeeView.app/Contents/Resources"; resources.mkdir(parents=True)
        (resources.parent / "MonoBundle").mkdir(); (resources.parent / "MonoBundle/Included.dll").write_bytes(b"MZ")
        (resources.parent / "Info.plist").write_bytes(plistlib.dumps({"CFBundleShortVersionString": "0.1.0"}))
        (root.parent / "LICENSE.md").write_text("NeeView original copyright")
        def git(arguments, **kwargs): return "abc123\n" if "rev-parse" in arguments else "?? portable/.DS_Store\n"
        with patch.object(package.subprocess, "check_output", side_effect=git):
            report = package.dependency_manifest(root, "NeeView.MacOS", resources, [{"RuntimePackName": "Runtime", "RuntimePackVersion": "1", "RuntimePackPath": str(runtime)}])
        self.assertEqual(["Included"], [item["name"] for item in report["dependencies"]])
        self.assertEqual(["Runtime"], [item["name"] for item in report["runtime_packs"]])
        self.assertTrue(report["worktree_dirty"]); self.assertFalse(report["tracked_source_dirty"])
        self.assertEqual(["MonoBundle/Included.dll"], report["dependencies"][0]["bundled_files"])
        self.assertEqual("NeeView original copyright", (resources / "LICENSE.md").read_text())

    def test_upstream_license_requires_exact_commit_and_original_bytes(self):
        root = self.root / "portable"
        original = root / "licenses/upstream/sample/COPYING"
        original.parent.mkdir(parents=True)
        original.write_text("actual copyright and terms")
        licenses = self.root / "Resources/licenses"
        record = {"name": "Sample", "version": "1", "repository_commit": "abc", "license_files": []}
        entry = {"repository_commit": "abc", "files": [{"path": "licenses/upstream/sample/COPYING", "upstream_path": "COPYING", "url": "https://example.test/abc/COPYING", "sha256": package.sha256(original)}]}
        package.add_upstream_licenses(record, root, licenses, {"sample/1": entry})
        self.assertEqual("included", record["license_material_state"])
        self.assertEqual(original.read_bytes(), (licenses / "sample-1/upstream/COPYING").read_bytes())
        record["repository_commit"] = "other"
        with self.assertRaisesRegex(RuntimeError, "源码提交"):
            package.add_upstream_licenses(record, root, licenses, {"sample/1": entry})
        record["repository_commit"] = "abc"
        original.write_text("tampered")
        with self.assertRaisesRegex(RuntimeError, "指纹"):
            package.add_upstream_licenses(record, root, licenses, {"sample/1": entry})
        entry["files"][0]["sha256"] = package.sha256(original)
        entry["files"][0]["upstream_path"] = "../../escape"
        with self.assertRaisesRegex(RuntimeError, "越界"):
            package.add_upstream_licenses(record, root, licenses, {"sample/1": entry})

    def test_duplicate_asset_ownership_is_rejected(self):
        root = self.root / "portable"
        assets_path = root / "src/NeeView.MacOS/obj/project.assets.json"
        assets_path.parent.mkdir(parents=True)
        source = self.make_package("first")
        self.make_package("second")
        assets_path.write_text(json.dumps({"targets": {"net10.0-macos/osx-arm64": {
            name + "/1": {"type": "package", "runtime": {"lib/net10.0/Shared.dll": {}}} for name in ("First", "Second")}},
            "libraries": {name + "/1": {"path": name.lower()} for name in ("First", "Second")},
            "packageFolders": {str(source.parent): {}}}))
        resources = self.root / "NeeView.app/Contents/Resources"
        resources.mkdir(parents=True)
        (resources.parent / "MonoBundle").mkdir()
        (resources.parent / "MonoBundle/Shared.dll").write_bytes(b"MZ")
        with self.assertRaisesRegex(RuntimeError, "同名资产"):
            package.dependency_manifest(root, "NeeView.MacOS", resources, [])

    def test_install_failure_restores_all_previous_artifacts(self):
        staged = self.root / "staged"
        artifacts = self.root / "artifacts"
        staged.mkdir(); artifacts.mkdir()
        names = ("NeeView.app", "NeeView.zip", "NeeView.report.json")
        for name in names:
            (staged / name).write_text("new " + name)
            (artifacts / name).write_text("old " + name)
        replace = package.os.replace
        def fail_report(source, destination):
            if source == staged / "NeeView.report.json":
                raise OSError("simulated replacement failure")
            replace(source, destination)
        with patch.object(package.os, "replace", side_effect=fail_report):
            with self.assertRaises(OSError):
                package.install_artifacts(staged, artifacts)
        for name in names:
            self.assertEqual("old " + name, (artifacts / name).read_text())

    def test_successful_install_replaces_complete_set(self):
        staged = self.root / "staged"
        artifacts = self.root / "artifacts"
        staged.mkdir(); artifacts.mkdir()
        for name in ("NeeView.app", "NeeView.zip", "NeeView.report.json"):
            (staged / name).write_text("new")
            (artifacts / name).write_text("old")
        package.install_artifacts(staged, artifacts)
        self.assertEqual(["new"] * 3, [path.read_text() for path in sorted(artifacts.iterdir())])

    def test_rollback_failure_keeps_recoverable_old_material_outside_stage(self):
        staged = self.root / "staged"
        artifacts = self.root / "artifacts"
        staged.mkdir(); artifacts.mkdir()
        for name in ("NeeView.app", "NeeView.zip", "NeeView.report.json"):
            (staged / name).write_text("new")
            (artifacts / name).write_text("old")
        replace = package.os.replace
        def fail_install_and_restore(source, destination):
            if source == staged / "NeeView.zip" or (source.name == "NeeView.app" and source.parent.name.startswith(".previous-package-")):
                raise OSError("simulated disk error")
            replace(source, destination)
        with patch.object(package.os, "replace", side_effect=fail_install_and_restore):
            with self.assertRaisesRegex(RuntimeError, "旧材料保留"):
                package.install_artifacts(staged, artifacts)
        backups = list(artifacts.glob(".previous-package-*"))
        self.assertEqual(1, len(backups))
        self.assertEqual("old", (backups[0] / "NeeView.app").read_text())

    def test_resource_with_matching_basename_is_not_attributed_as_runtime(self):
        root = self.root / "portable"
        assets_path = root / "src/NeeView.MacOS/obj/project.assets.json"
        assets_path.parent.mkdir(parents=True)
        assets_path.write_text(json.dumps({"targets": {"net10.0-macos/osx-arm64": {
            "NotShipped/1": {"type": "package", "runtime": {"lib/net10.0/Shared.dll": {}}}}},
            "libraries": {}, "packageFolders": {}}))
        runtime = self.make_package("runtime")
        resources = self.root / "NeeView.app/Contents/Resources"
        resources.mkdir(parents=True)
        (resources / "Shared.dll").write_bytes(b"unrelated resource")
        (resources.parent / "Info.plist").write_bytes(plistlib.dumps({"CFBundleShortVersionString": "0.1.0"}))
        (root.parent / "LICENSE.md").write_text("original terms")
        with patch.object(package.subprocess, "check_output", return_value=""):
            result = package.dependency_manifest(root, "NeeView.MacOS", resources, [{"RuntimePackName": "Runtime", "RuntimePackVersion": "1", "RuntimePackPath": str(runtime)}])
        self.assertEqual([], result["dependencies"])


if __name__ == "__main__":
    unittest.main()
