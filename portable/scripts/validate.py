#!/usr/bin/env python3
"""串行检查源码出处、Engine 和正式视图的 Headless 测试；正式应用构建单独记录。"""
import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import subprocess
import xml.etree.ElementTree as ET


def check_boundaries(root):
    """验证三个生产项目及迁入源文件指纹，不将静态检查当作 Windows 运行验收。"""
    projects = sorted(path.parent.name for path in (root / "src").glob("*/*.csproj"))
    if projects != ["NeeView.Backends", "NeeView.Engine", "NeeView.MacOS"]:
        raise RuntimeError("生产项目必须只包含 Engine/Backends/MacOS")
    manifest = json.loads((root / "docs/source-migration.json").read_text())
    for group in ("files", "partial_adapters", "ui_sources"):
        for item in manifest[group]:
            source = root.parent / item["source"]
            if hashlib.sha256(source.read_bytes()).hexdigest() != item["sha256"]:
                raise RuntimeError("固定 Windows 源码变化：" + item["source"])
            if not (root / item["target"]).is_file():
                raise RuntimeError("缺少迁移目标：" + item["target"])
    # 原依赖库按基线 gitlink 锁定；源码内嵌进 Engine，保持三个生产项目。
    for library in manifest.get("upstream_sources", []):
        entry = subprocess.check_output(["git", "ls-tree", manifest["baseline"], library["gitlink"]], cwd=root.parent, text=True)
        if entry.split()[2] != library["commit"]:
            raise RuntimeError("原依赖库提交不一致：" + library["gitlink"])
        for item in library["files"]:
            expected = item.get("adapted_sha256", item["sha256"])
            if hashlib.sha256((root / item["target"]).read_bytes()).hexdigest() != expected:
                raise RuntimeError("迁入依赖源码变化未更新出处：" + item["target"])
    forbidden = ("using Avalonia", "using System.Windows", "using AppKit", "using Foundation", "using ImageMagick", "using SharpCompress")
    for path in (root / "src/NeeView.Engine").rglob("*.cs"):
        if "obj" in path.parts or "bin" in path.parts:
            continue
        if any(value in path.read_text() for value in forbidden):
            raise RuntimeError("Engine 包含平台依赖：" + str(path))
    for area in ("Views", "ViewModels"):
        for path in (root / "src/NeeView.MacOS" / area).rglob("*.cs"):
            if any(value in path.read_text() for value in ("NeeView.Backends", "ImageMagick", "SharpCompress", "Directory.Enumerate")):
                raise RuntimeError("视图直接依赖后端：" + str(path))
    return {"production_projects": projects, "source_files": len(manifest["files"]), "partial_adapters": len(manifest["partial_adapters"]),
            "upstream_source_files": sum(len(library["files"]) for library in manifest.get("upstream_sources", []))}


def main():
    """接收 SDK 路径及可选正式构建开关，写入各步骤真实退出状态。"""
    parser = argparse.ArgumentParser()
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--macos-source", action="store_true", help="仅验证正式入口编译，需 macOS workload；不是应用构建")
    parser.add_argument("--macos", action="store_true", help="执行正式应用构建，需匹配的完整 Xcode")
    parser.add_argument("--macos-native", action="store_true", help="后台运行官方macOS绑定的独立测试.app，不启动正式界面")
    parser.add_argument("--phase", choices=(
        "p5-profile-commands", "p5-slideshow", "p5-animated-images", "p5-compressed-password", "p5-pdf-password", "p5-pdf-filetypes", "p5-pdf", "p5-nested", "p5-settings-search", "p5-theme-folder", "p5-fonts", "p5-theme", "p5-profile-assets", "p5-effect-compatibility", "p5-difference-settings", "p5-folder-compatibility", "p5-legacy-compatibility", "p5-profile-apply", "p5-profile-preview", "p34-device", "p4-completion", "p4-logical-book-copy",
        "p4-directory-copy", "p4-book-transfer", "p4-delete-book", "p4-multi-paste", "p4-realization",
        "p4-clipboard", "p4-rename", "p4-delete", "p4-destination", "p4-multipage",
        "p3-navigation-completion", "p3-index", "p3-panorama", "p3-completion", "p3-performance",
        "p3-navigation", "p3-browse", "p1", "p2", "p2-docking",
        "p2-selection", "p2-bookshelf", "p2-bookmark", "p2-history", "p2-slider",
        "p2-playlist", "p2-autohide", "p2-bookmark-navigation", "p2-bookmark-search", "p2-history-search",
        "p2-history-retention", "p2-folder-parameters", "p2-book-hierarchy", "p2-mouse-input", "p2-view-transform",
        "p2-book-controls", "p2-bookshelf-bookmarks", "p2-history-policy", "p2-list-templates", "p2-floating",
        "p2-direction-gestures", "p2-animation", "p2-resources", "p2-completion", "p2-device-input",
        "p2-device-resources",
    ), default="p1", help="独立保存当前阶段的构建与测试证据")
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    env = os.environ.copy(); env.pop("DOTNET_ROOT", None)
    # 测试截图使用当前节点标签，避免新一轮验证覆盖历史阶段证据。
    env["NEEVIEW_ACCEPTANCE_PHASE"] = args.phase
    report = {"utc": datetime.now(timezone.utc).isoformat(), "steps": [], "scope": args.phase + " 原算法、资源、来源/导航及正式XAML Headless；不代表真机/Windows对照", "formal_app": "未执行"}

    def run(label, command):
        """使用默认 bin/obj 顺序执行；失败原样记录并停止，禁止改输出目录。"""
        result = subprocess.run([str(arg) for arg in command], cwd=root, env=env, text=True, capture_output=True)
        report["steps"].append({"name": label, "exit_code": result.returncode, "output": result.stdout + result.stderr})
        print(f"{label}: {'通过' if result.returncode == 0 else '失败'}", flush=True)
        if result.returncode:
            print(result.stdout + result.stderr)
            raise RuntimeError(label + "未通过")

    try:
        report["boundaries"] = check_boundaries(root)
        run("Engine 构建", [args.dotnet, "build", "src/NeeView.Engine/NeeView.Engine.csproj", "-m:1", "-p:RestoreLockedMode=true"])
        run("原算法与正式视图测试", [args.dotnet, "test", "tests/NeeView.Engine.Tests/NeeView.Engine.Tests.csproj", "-m:1", "-p:RestoreLockedMode=true", "--logger", "trx;LogFileName=engine.trx"])
        ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
        counters = ET.parse(root / "tests/NeeView.Engine.Tests/TestResults/engine.trx").find("t:ResultSummary/t:Counters", ns)
        report["tests"] = counters.attrib
        if args.macos_native:
            # macOS workload使用自身原生引导；xUnit仍要求UseAppHost=true。仅此测试构建传入，不改变产品构建。
            run("官方macOS绑定测试构建", [args.dotnet, "build", "tests/NeeView.Backends.MacOS.Tests/NeeView.Backends.MacOS.Tests.csproj", "-m:1", "-p:RestoreLockedMode=true", "-p:UseAppHost=true"])
            native_root = root / "tests/NeeView.Backends.MacOS.Tests"
            native_results = native_root / "TestResults/native.xml"
            native_results.parent.mkdir(exist_ok=True)
            native = native_root / "bin/Debug/net10.0-macos/NeeView.Backends.MacOS.Tests.app/Contents/MacOS/NeeView.Backends.MacOS.Tests"
            run("实际macOS后端测试（无窗口）", [native, "-noLogo", "-parallel", "none", "-xml", native_results])
            report["native_tests"] = ET.parse(native_results).find("assembly").attrib
        if args.macos_source:
            run("正式入口源码编译（Library检查）", [args.dotnet, "build", "src/NeeView.MacOS/NeeView.MacOS.csproj", "-m:1", "-p:OutputType=Library", "-p:RestoreLockedMode=true"])
        if args.macos:
            report["formal_app"] = "构建失败或中断，详见步骤"
            run("正式 Mac 应用构建", [args.dotnet, "build", "src/NeeView.MacOS/NeeView.MacOS.csproj", "-m:1", "-p:RestoreLockedMode=true"])
            # SDK 的 RID 子目录还包含签名前的中间 .app；只检查默认输出目录的最终成品。
            app = root / "src/NeeView.MacOS/bin/Debug/net10.0-macos/NeeView.MacOS.app"
            run("正式 .app 本地签名校验", ["codesign", "--verify", "--deep", "--strict", "--verbose=2", app])
            report["app_bundle"] = str(app)
            report["formal_app"] = "构建及本地签名校验通过；运行见独立真机记录，Developer ID/公证/安装未执行"
    finally:
        (root / "acceptance").mkdir(exist_ok=True)
        (root / "acceptance" / (args.phase + "-validation.json")).write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n")


if __name__ == "__main__":
    main()
