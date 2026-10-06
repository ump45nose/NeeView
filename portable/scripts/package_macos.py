#!/usr/bin/env python3
"""使用标准构建目录发布 ARM64 .app，并生成实际依赖清单及可选签名公证。"""
import argparse
import json
import hashlib
import os
from pathlib import Path
import shutil
import subprocess
import plistlib
import tempfile
import xml.etree.ElementTree as ET


def run(argv, cwd):
    """以独立参数执行工具，失败立即停止，禁止切换默认构建输出。"""
    env = os.environ.copy()
    env.pop("DOTNET_ROOT", None)
    subprocess.run([str(arg) for arg in argv], cwd=cwd, env=env, check=True)


MACHO_MAGIC = {b"\xcf\xfa\xed\xfe", b"\xfe\xed\xfa\xcf", b"\xce\xfa\xed\xfe", b"\xfe\xed\xfa\xce", b"\xca\xfe\xba\xbe", b"\xbe\xba\xfe\xca", b"\xca\xfe\xba\xbf", b"\xbf\xba\xfe\xca"}


def sha256(path):
    """流式计算完整文件指纹，避免原生库整体复制到内存。"""
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def native_binaries(app):
    """按Mach-O头识别主程序、框架和动态库；同一实体只签名一次。"""
    for path in sorted(app.rglob("*")):
        if path.is_file() and not path.is_symlink():
            with path.open("rb") as stream:
                if stream.read(4) in MACHO_MAGIC:
                    yield path


def copy_licenses(package, destination):
    """保持包内相对目录，重名文件不覆盖，第三方NOTICE也进入成品。"""
    files = []
    for path in sorted(package.rglob("*")):
        name = path.name.lower()
        if path.is_file() and (name.startswith(("license", "notice", "copyright")) or name.startswith(("third-party", "third_party", "thirdparty"))):
            relative = path.relative_to(package)
            target = destination / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(path, target)
            files.append({"source": relative.as_posix(), "file": target.relative_to(destination.parent.parent).as_posix(), "sha256": sha256(target)})
    return files


def package_record(name, version, package, licenses):
    """以实际包元数据和原文许可建立记录；不把SPDX表达式冒充完整许可材料。"""
    nuspec = next(package.glob("*.nuspec"), None)
    if nuspec is None:
        raise RuntimeError(f"无法找到实际依赖许可元数据：{name}/{version}")
    record = {"name": name, "version": version}
    destination = licenses / f"{name.lower()}-{version}"
    record["license_files"] = copy_licenses(package, destination)
    for element in ET.parse(nuspec).iter():
        tag = element.tag.rsplit("}", 1)[-1]
        if tag in {"license", "licenseUrl", "projectUrl", "repository", "copyright", "authors"}:
            record[tag] = element.text if tag != "repository" else element.attrib.get("url")
            if tag == "license":
                record["license_type"] = element.attrib.get("type")
                if element.attrib.get("type") == "file":
                    source = (package / (element.text or "")).resolve()
                    if not source.is_relative_to(package.resolve()) or not source.is_file():
                        raise RuntimeError(f"实际依赖声明的许可文件缺失：{name}/{version}")
                    relative = source.relative_to(package.resolve())
                    if not any(file["source"] == relative.as_posix() for file in record["license_files"]):
                        target = destination / relative; target.parent.mkdir(parents=True, exist_ok=True); shutil.copy2(source, target)
                        record["license_files"].append({"source": relative.as_posix(), "file": target.relative_to(licenses.parent).as_posix(), "sha256": sha256(target)})
            if tag == "repository": record["repository_commit"] = element.attrib.get("commit")
    record["license_material_state"] = "included" if record["license_files"] else "metadata_only"
    return record


def add_upstream_licenses(record, root, licenses, upstream):
    """缺少包内原文时使用相同NuGet版本/源码提交的已核验材料，发布期间不联网。"""
    entry = upstream.get(f"{record['name'].lower()}/{record['version']}")
    if entry is None: return
    if entry["repository_commit"] != record.get("repository_commit"):
        raise RuntimeError("依赖源码提交与许可材料不一致：" + record["name"])
    for material in entry["files"]:
        source = (root / material["path"]).resolve()
        if not source.is_relative_to((root / "licenses").resolve()) or sha256(source) != material["sha256"]:
            raise RuntimeError("上游许可原文指纹不一致：" + record["name"])
        destination = licenses / f"{record['name'].lower()}-{record['version']}" / "upstream"
        target = destination / material["upstream_path"]
        if not target.resolve().is_relative_to(destination.resolve()):
            raise RuntimeError("上游许可目标越界：" + record["name"])
        target.parent.mkdir(parents=True, exist_ok=True); shutil.copy2(source, target)
        record["license_files"].append({"source": material["url"], "file": target.relative_to(licenses.parent).as_posix(), "sha256": material["sha256"]})
    record["license_material_state"] = "included"


def source_algorithm_records(root, licenses):
    """记录纯源码算法及其固定提交/许可原文，不联网解析来源。"""
    migration = json.loads((root / "docs/source-migration.json").read_text())
    records = []
    for reference in migration.get("external_algorithm_references", []):
        relative = Path(reference["license"])
        source = (root / relative).resolve()
        if not source.is_relative_to((root / "licenses").resolve()) or not source.is_file():
            raise RuntimeError("源码算法许可文件缺失：" + reference["name"])
        actual_sha256 = sha256(source)
        if actual_sha256 != reference["license_sha256"]:
            raise RuntimeError("源码算法许可原文指纹不一致：" + reference["name"])
        destination = licenses / relative.relative_to("licenses")
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, destination)
        records.append({
            "name": reference["name"],
            "repository_commit": reference["repository_commit"],
            "repository": reference["url"],
            "source_files": reference["source_files"],
            "target": reference["target"],
            "license": reference["license"],
            "license_files": [{"source": reference["license"], "file": destination.relative_to(licenses.parent).as_posix(), "sha256": actual_sha256}],
            "license_material_state": "included",
        })
    return records


def dependency_manifest(root, project, resources, framework_references):
    """将ARM64已解析runtime/native资产与实际.app交叉核对，包含SDK运行时许可。"""
    assets = json.loads((root / "src" / project / "obj/project.assets.json").read_text())
    targets = [value for key, value in assets["targets"].items() if key.endswith("/osx-arm64")]
    if len(targets) != 1:
        raise RuntimeError("缺少唯一ARM64还原图")
    bundled = {}
    # 本项目的SDK将NuGet runtime/native扁平发布到MonoBundle；Resources不参与归属。
    # 其余位置的原生文件仍由完整Mach-O清单覆盖，不能按任意同名文件推断包来源。
    for path in (resources.parent / "MonoBundle").rglob("*"):
        if path.is_file(): bundled.setdefault(path.name, []).append(path)
    licenses = resources / "licenses"; licenses.mkdir(parents=True, exist_ok=True)
    records = []
    owners = {}
    upstream_path = root / "licenses/upstream.json"
    upstream = json.loads(upstream_path.read_text()) if upstream_path.is_file() else {}
    for identity, value in targets[0].items():
        if value.get("type") != "package": continue
        names = {Path(asset).name for kind in ("runtime", "native", "runtimeTargets") for asset in value.get(kind, {}) if Path(asset).name != "_._"}
        actual = sorted({path.relative_to(resources.parent).as_posix() for name in names for path in bundled.get(name, [])})
        if not actual: continue  # 构建任务及其他平台native包不作为已分发依赖。
        for name in names & bundled.keys():
            if name in owners and owners[name] != identity:
                raise RuntimeError(f"实际依赖同名资产无法可靠归属：{name} ({owners[name]}, {identity})")
            owners[name] = identity
        name, version = identity.rsplit("/", 1)
        location = assets["libraries"][identity]["path"]
        candidates = [Path(folder) / location for folder in assets["packageFolders"] if (Path(folder) / location).is_dir()]
        if len(candidates) != 1: raise RuntimeError(f"无法唯一定位实际包：{identity}")
        record = package_record(name, version, candidates[0], licenses)
        add_upstream_licenses(record, root, licenses, upstream)
        record["bundled_files"] = actual; records.append(record)
    runtime_records = []
    for reference in framework_references:
        location = reference.get("RuntimePackPath")
        if not location: continue
        package = Path(location)
        record = package_record(reference["RuntimePackName"], reference["RuntimePackVersion"], package, licenses)
        if (record["name"], record["version"]) not in {(item["name"], item["version"]) for item in runtime_records}: runtime_records.append(record)
    if not runtime_records: raise RuntimeError("发布缺少SDK运行时许可来源")
    migration_path = root / "docs/source-migration.json"
    source_records = source_algorithm_records(root, licenses) if migration_path.is_file() else []
    commit = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=root, text=True).strip()
    status = subprocess.check_output(["git", "status", "--porcelain"], cwd=root, text=True).splitlines()
    version = plistlib.loads((resources.parent / "Info.plist").read_bytes())["CFBundleShortVersionString"]
    manifest = {"version": version, "commit": commit, "worktree_dirty": bool(status), "tracked_source_dirty": any(not item.startswith("??") for item in status), "baseline": "c5c398d89", "host": project,
                "dependency_source": "ARM64 project.assets.json runtime/native matched to published bundle; resolved SDK runtime packs",
                "native_hash_scope": "bundle before signing; signed archive hashes recorded separately",
                "dependencies": sorted(records, key=lambda item: item["name"].lower()), "runtime_packs": runtime_records,
                "source_algorithm_references": source_records,
                "native_files": [{"path": path.relative_to(resources.parent).as_posix(), "unsigned_sha256": sha256(path)} for path in native_binaries(resources.parent)]}
    (resources / "dependencies.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n")
    shutil.copy2(root.parent / "LICENSE.md", resources / "LICENSE.md")
    return manifest


def inspect_bundle(app):
    """验证所有Mach-O包含ARM64及本地签名；不宣称Gatekeeper或真机使用通过。"""
    binaries = []
    for path in native_binaries(app):
        architectures = subprocess.check_output(["lipo", "-archs", path], text=True).strip().split()
        if "arm64" not in architectures: raise RuntimeError("成品缺少ARM64：" + str(path.relative_to(app)))
        run(["codesign", "--verify", "--strict", path], app.parent)
        binaries.append({"path": path.relative_to(app).as_posix(), "architectures": architectures, "signed_sha256": sha256(path)})
    if not binaries: raise RuntimeError("成品没有原生启动程序")
    run(["codesign", "--verify", "--deep", "--strict", app], app.parent)
    return binaries


def install_artifacts(staged, artifacts):
    """所有校验成功后替换稳定成品；替换失败时恢复已有应用、ZIP和报告。"""
    names = ("NeeView.app", "NeeView.zip", "NeeView.report.json")
    # 旧材料置于stage之外，进程中断或回滚本身失败时不会被stage自动清理。
    backup = Path(tempfile.mkdtemp(prefix=".previous-package-", dir=artifacts))
    moved = []
    installed = []
    try:
        for name in names:
            destination = artifacts / name
            if destination.exists():
                os.replace(destination, backup / name)
                moved.append(name)
            os.replace(staged / name, destination)
            installed.append(name)
    except BaseException as failure:
        errors = []
        for name in reversed(installed):
            try:
                os.replace(artifacts / name, staged / name)
            except OSError as error:
                errors.append(str(error))
        for name in reversed(moved):
            try:
                os.replace(backup / name, artifacts / name)
            except OSError as error:
                errors.append(str(error))
        if errors:
            raise RuntimeError(f"成品替换/回滚失败，旧材料保留于 {backup}: {errors}") from failure
        shutil.rmtree(backup)
        raise
    shutil.rmtree(backup)


def prepare_artifacts(root, source, resolved, staged, args):
    """在隔离工件目录装配并验证，不改变SDK默认bin/obj和已有可用成品。"""
    app = staged / "NeeView.app"
    shutil.copytree(source, app, symlinks=True)
    resources = app / "Contents/Resources"
    resources.mkdir(exist_ok=True)
    manifest = dependency_manifest(root, "NeeView.MacOS", resources, resolved["Items"]["ResolvedFrameworkReference"])
    identity = args.sign or "-"
    signing = ["codesign", "--force", "--timestamp" if args.sign else "--timestamp=none", "--options", "runtime", "--sign", identity, "--entitlements", root / "scripts/entitlements.plist"]
    for binary in sorted(native_binaries(app), key=lambda path: len(path.parts), reverse=True):
        run(signing + [binary], root)
    for framework in sorted(app.rglob("*.framework"), key=lambda path: len(path.parts), reverse=True):
        run(signing + [framework], root)
    run(signing + [app], root)
    binaries = inspect_bundle(app)
    archive = app.with_suffix(".zip")
    run(["ditto", "-c", "-k", "--sequesterRsrc", "--keepParent", app, archive], root)
    if args.notary_profile:
        run(["xcrun", "notarytool", "submit", archive, "--keychain-profile", args.notary_profile, "--wait"], root)
        run(["xcrun", "stapler", "staple", app], root)
        run(["spctl", "--assess", "--type", "execute", app], root)
        archive.unlink()
        run(["ditto", "-c", "-k", "--sequesterRsrc", "--keepParent", app, archive], root)
        binaries = inspect_bundle(app)
    run(["unzip", "-t", archive], root)
    # 随机隔离目录验证ZIP完整性和重定位后的签名，不改/打开用户Applications。
    with tempfile.TemporaryDirectory(prefix="neeview-package-") as temporary:
        run(["ditto", "-x", "-k", archive, temporary], root)
        relocated = inspect_bundle(Path(temporary) / app.name)
        if binaries != relocated:
            raise RuntimeError("ZIP解包后的原生文件指纹变化")
    report = {"version": manifest["version"], "commit": manifest["commit"], "worktree_dirty": manifest["worktree_dirty"], "tracked_source_dirty": manifest["tracked_source_dirty"],
              "architecture": "osx-arm64", "self_contained": True, "signing": "Developer ID" if args.sign else "ad-hoc development", "notarized": bool(args.notary_profile),
              "archive_sha256": sha256(archive), "native_files": binaries, "zip_relocation_and_signature": "passed", "interactive_installation": "not executed", "gatekeeper": "passed" if args.notary_profile else "not executed",
              "license_metadata_only": [item["name"] for item in manifest["dependencies"] + manifest["runtime_packs"] if item["license_material_state"] == "metadata_only"]}
    archive.with_suffix(".report.json").write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n")


def main():
    """发布、装配、签名、公证按顺序执行；无凭据时生成明确的开发成品。"""
    parser = argparse.ArgumentParser()
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--sign")
    parser.add_argument("--notary-profile")
    args = parser.parse_args()
    if args.sign and not args.sign.startswith("Developer ID Application:"):
        raise RuntimeError("正式分发要求明确的 Developer ID Application 签名身份")
    if args.notary_profile and not args.sign:
        raise RuntimeError("公证要求明确的 Developer ID Application 签名身份")
    root = Path(__file__).resolve().parents[1]
    project = "NeeView.MacOS"
    project_dir = root / "src" / project
    run([args.dotnet, "publish", project_dir / f"{project}.csproj", "-c", "Release", "-r", "osx-arm64",
         "--self-contained", "true", "-m:1", "-p:RestoreLockedMode=true", "-p:EnableCodeSigning=false"], root)
    # 读取本次SDK实际落点，不用rglob误选RID中间.app或旧构建。
    env = os.environ.copy()
    env.pop("DOTNET_ROOT", None)
    resolved = json.loads(subprocess.check_output([args.dotnet, "msbuild", str(project_dir / f"{project}.csproj"), "-t:_GenerateBundleName;_ComputeFrameworkVariables", "-p:Configuration=Release", "-p:RuntimeIdentifier=osx-arm64", "-p:SelfContained=true", "-p:EnableCodeSigning=false", "-getProperty:AppBundleDir", "-getItem:ResolvedFrameworkReference"], cwd=root, env=env, text=True))
    source = project_dir / resolved["Properties"]["AppBundleDir"]
    if not source.is_dir():
        raise RuntimeError("SDK报告的正式.app不存在")
    if not source.resolve().is_relative_to((project_dir / "bin/Release").resolve()):
        raise RuntimeError("SDK报告的.app不在本次默认Release输出树")
    info = plistlib.loads((source / "Contents/Info.plist").read_bytes())
    if info.get("CFBundleExecutable") != project:
        raise RuntimeError("SDK报告的.app启动程序与正式项目不一致")
    artifacts = root / "artifacts"
    artifacts.mkdir(exist_ok=True)
    with tempfile.TemporaryDirectory(prefix=".package-", dir=artifacts) as temporary:
        staged = Path(temporary)
        prepare_artifacts(root, source, resolved, staged, args)
        install_artifacts(staged, artifacts)
    print(artifacts / "NeeView.app")


if __name__ == "__main__":
    main()
