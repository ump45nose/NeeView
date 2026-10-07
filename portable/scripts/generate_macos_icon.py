#!/usr/bin/env python3
"""用 macOS 内置工具将原 NeeView 矢量图标转换为 Finder/Dock 多尺寸资源。"""
from pathlib import Path
import shutil
import subprocess
import tempfile
import xml.etree.ElementTree as ET


def generate_icon(source, destination):
    """保持 source 的 viewBox/图形，在各原生分辨率栅格化并写出 destination ICNS。"""
    tree = ET.parse(source)
    variants = [(16, 1), (16, 2), (32, 1), (32, 2), (128, 1),
                (128, 2), (256, 1), (256, 2), (512, 1), (512, 2)]
    with tempfile.TemporaryDirectory(prefix="neeview-icon-") as temporary:
        root = Path(temporary)
        iconset = root / "AppIcon.iconset"
        iconset.mkdir()
        for size, scale in variants:
            pixels = size * scale
            # 改变栅格输出尺寸而不缩放已有小图，Retina 资源直接来自原矢量路径。
            tree.getroot().set("width", str(pixels))
            tree.getroot().set("height", str(pixels))
            svg = root / "AppIcon.svg"
            tree.write(svg, encoding="utf-8", xml_declaration=True)
            suffix = "@2x" if scale == 2 else ""
            png = iconset / f"icon_{size}x{size}{suffix}.png"
            subprocess.run(["/usr/bin/sips", "-s", "format", "png", str(svg),
                            "--out", str(png)], check=True, capture_output=True)
        result = root / "AppIcon.icns"
        subprocess.run(["/usr/bin/iconutil", "-c", "icns", str(iconset),
                        "-o", str(result)], check=True)
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(result, destination)


def main():
    """从仓库原 AppList SVG 再生正式资源；运行时及普通构建不依赖此工具。"""
    root = Path(__file__).resolve().parents[2]
    source = root / "MakePackage/Appx/Icons/Sources/AppList.targetsize-256.svg"
    destination = root / "portable/src/NeeView.MacOS/Styles/AppIcon.icns"
    generate_icon(source, destination)
    print(destination)


if __name__ == "__main__":
    main()
