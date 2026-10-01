#!/usr/bin/env python3
"""串行复现模块测试、共享窗口构建和真实归档解码；不替代真机或正式发布验收。"""
import argparse
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import subprocess
import xml.etree.ElementTree as ET


def main():
    """接收隔离 SDK 路径和可选性能开关，输出分步骤证据与测试数量。"""
    parser = argparse.ArgumentParser()
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--benchmarks", action="store_true")
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    env = os.environ.copy()
    env.pop("DOTNET_ROOT", None)
    report = {"utc": datetime.now(timezone.utc).isoformat(), "steps": [], "scope": "模块/Headless/服务链路；不包含真实滚动帧或签名公证"}
    ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}

    def run(label, command):
        """逐个运行并记录退出码；失败停止，使用 SDK 默认输出目录。"""
        result = subprocess.run([str(arg) for arg in command], cwd=root, env=env, text=True, capture_output=True)
        report["steps"].append({"name": label, "exit_code": result.returncode, "output": result.stdout + result.stderr})
        print(f"{label}: {'通过' if result.returncode == 0 else '失败'}", flush=True)
        if result.returncode:
            print(result.stdout + result.stderr)
            raise RuntimeError(label + "未通过")

    try:
        run("Preview 构建", [args.dotnet, "build", "src/NeeView.Preview/NeeView.Preview.csproj", "-m:1", "-p:RestoreLockedMode=true"])
        run("自动测试", [args.dotnet, "test", "tests/NeeView.Portable.Tests/NeeView.Portable.Tests.csproj", "-m:1", "-p:RestoreLockedMode=true", "--logger", "trx;LogFileName=portable.trx"])
        counters = ET.parse(root / "tests/NeeView.Portable.Tests/TestResults/portable.trx").find("t:ResultSummary/t:Counters", ns)
        report["tests"] = counters.attrib
        executable = root / "src/NeeView.Preview/bin/Debug/net10.0/NeeView.Preview.dll"
        fixtures = root / "artifacts/acceptance-images"
        if not fixtures.exists():
            run("4K 夹具生成", [args.dotnet, executable, "--make-fixtures", fixtures])
        sources = [fixtures, fixtures.with_suffix(".cbz"), *[root / "tests/NeeView.Portable.Tests/Fixtures" / name for name in ("Rar.rar", "Rar.solid.rar", "Rar5.solid.rar", "7Zip.LZMA.7z", "7Zip.solid.7z")]]
        for source in sources:
            run("真实解码/恢复 " + source.name, [args.dotnet, executable, "--smoke", source])
        if args.benchmarks:
            for source, name in ((fixtures, "directory"), (fixtures.with_suffix(".cbz"), "zip"), (sources[3], "solid-rar"), (sources[-1], "solid-7z")):
                run("服务性能 " + name, [args.dotnet, executable, "--benchmark", source, root / "acceptance" / (name + "-benchmark.json")])
    finally:
        # 成功和失败都保留分层结果，不把后续未执行步骤记为通过。
        (root / "acceptance/latest-validation.json").write_text(json.dumps(report, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
