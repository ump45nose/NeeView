#!/usr/bin/env python3
"""使用标准构建目录发布 ARM64 .app，并生成实际依赖清单及可选签名公证。"""
import argparse
import json
import hashlib
import os
from pathlib import Path
import shutil
import subprocess
import xml.etree.ElementTree as ET


def run(argv, cwd):
    """以独立参数执行工具，失败立即停止，禁止切换默认构建输出。"""
    env = os.environ.copy()
    env.pop("DOTNET_ROOT", None)
    subprocess.run([str(arg) for arg in argv], cwd=cwd, env=env, check=True)


def dependency_manifest(root, project, resources):
    """读取实际 lock 文件和 NuGet 元数据，保留托管和原生依赖的许可出处。"""
    records = {}
    dependencies_file = next(resources.parent.rglob(f"{project}.deps.json"), None)
    if dependencies_file is None:
        raise RuntimeError("发布目录缺少实际依赖清单")
    for identity, library in json.loads(dependencies_file.read_text()).get("libraries", {}).items():
        if library.get("type") != "package":
            continue
        name, version = identity.rsplit("/", 1)
        records[(name.lower(), version)] = {"name": name, "version": version}
    licenses = resources / "licenses"
    licenses.mkdir(parents=True, exist_ok=True)
    for (name, version), record in records.items():
        package = Path.home() / ".nuget/packages" / name / version
        nuspec = next(package.glob("*.nuspec"), None)
        if nuspec is None:
            raise RuntimeError(f"无法找到实际依赖的许可元数据：{name}/{version}")
        if nuspec:
            tree = ET.parse(nuspec)
            for element in tree.iter():
                tag = element.tag.rsplit("}", 1)[-1]
                if tag in {"license", "licenseUrl", "projectUrl", "repository"}:
                    record[tag] = element.text if tag != "repository" else element.attrib.get("url")
                    if tag == "license" and element.attrib.get("type") == "file":
                        source = package / (element.text or "")
                        if source.is_file():
                            shutil.copy2(source, licenses / f"{name}-{version}-{source.name}")
        # 包中的 NOTICE/LICENSE 材料保留，原生 codec 说明随所属包一起交付。
        for file in package.rglob("*"):
            if file.is_file() and file.name.lower().startswith(("license", "notice", "copyright")):
                shutil.copy2(file, licenses / f"{name}-{version}-{file.name}")
    commit = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=root, text=True).strip()
    dirty = bool(subprocess.check_output(["git", "status", "--porcelain"], cwd=root, text=True).strip())
    manifest = {"version": "0.1.0", "commit": commit, "worktree_dirty": dirty, "baseline": "c5c398d89", "host": project,
                "native_hash_scope": "publish output before app signing; codesign may change Mach-O bytes",
                "dependencies": sorted(records.values(), key=lambda item: item["name"].lower()),
                "native_files": [{"path": str(file.relative_to(resources.parent)), "unsigned_sha256": hashlib.sha256(file.read_bytes()).hexdigest()} for file in resources.parent.rglob("*") if file.is_file() and file.suffix in {".dylib", ".so"}]}
    (resources / "dependencies.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2))
    shutil.copy2(root.parent / "LICENSE.md", resources / "LICENSE.md")


def main():
    """发布、装配、签名、公证按顺序执行；无凭据时生成明确的开发成品。"""
    parser = argparse.ArgumentParser()
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--sign")
    parser.add_argument("--notary-profile")
    args = parser.parse_args()
    if args.notary_profile and (not args.sign or not args.sign.startswith("Developer ID Application:")):
        raise RuntimeError("公证要求明确的 Developer ID Application 签名身份")
    root = Path(__file__).resolve().parents[1]
    project = "NeeView.MacOS"
    project_dir = root / "src" / project
    run([args.dotnet, "publish", project_dir / f"{project}.csproj", "-c", "Release", "-r", "osx-arm64",
         "--self-contained", "true", "-m:1", "-p:RestoreLockedMode=true", "-p:EnableCodeSigning=false"], root)
    artifacts = root / "artifacts"
    artifacts.mkdir(exist_ok=True)
    app = artifacts / "NeeView.app"
    if app.exists():
        shutil.rmtree(app)
    candidates = list((project_dir / "bin/Release").rglob(f"{project}.app"))
    if len(candidates) != 1:
        raise RuntimeError("无法唯一定位 SDK 生成的正式 .app，发布未完成")
    shutil.copytree(candidates[0], app, symlinks=True)
    resources = app / "Contents/Resources"
    resources.mkdir(exist_ok=True)
    dependency_manifest(root, project, resources)
    if args.sign:
        for binary in sorted(app.rglob("*"), key=lambda path: len(path.parts), reverse=True):
            if binary.is_file() and binary.read_bytes()[:4] in {b"\xcf\xfa\xed\xfe", b"\xfe\xed\xfa\xcf", b"\xca\xfe\xba\xbe", b"\xbe\xba\xfe\xca"}:
                run(["codesign", "--force", "--timestamp", "--options", "runtime", "--sign", args.sign, "--entitlements", root / "scripts/entitlements.plist", binary], root)
        run(["codesign", "--force", "--timestamp", "--options", "runtime", "--entitlements", root / "scripts/entitlements.plist", "--sign", args.sign, app], root)
        run(["codesign", "--verify", "--deep", "--strict", app], root)
    archive = app.with_suffix(".zip")
    if archive.exists():
        archive.unlink()
    run(["ditto", "-c", "-k", "--sequesterRsrc", "--keepParent", app, archive], root)
    if args.notary_profile:
        if not args.sign:
            raise RuntimeError("公证要求 Developer ID 签名")
        run(["xcrun", "notarytool", "submit", archive, "--keychain-profile", args.notary_profile, "--wait"], root)
        run(["xcrun", "stapler", "staple", app], root)
        run(["spctl", "--assess", "--type", "execute", app], root)
        archive.unlink()
        run(["ditto", "-c", "-k", "--sequesterRsrc", "--keepParent", app, archive], root)
    print(app)


if __name__ == "__main__":
    main()
