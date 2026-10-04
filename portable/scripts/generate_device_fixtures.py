#!/usr/bin/env python3
"""生成两端共用的编号分页、长图和设备像素夹具；只使用标准库。"""
import argparse
import binascii
import hashlib
import json
from pathlib import Path
import struct
import zipfile
import zlib

DIGITS = (
    ("111", "101", "101", "101", "111"), ("010", "110", "010", "010", "111"),
    ("111", "001", "111", "100", "111"), ("111", "001", "111", "001", "111"),
    ("101", "101", "111", "001", "001"), ("111", "100", "111", "001", "111"),
    ("111", "100", "111", "101", "111"), ("111", "001", "010", "010", "010"),
    ("111", "101", "111", "101", "111"), ("111", "101", "111", "001", "111"),
)


def write_png(path, width, height, row):
    """逐行写RGB PNG，限制生成内存；row返回当前行的RGB字节。"""
    def chunk(output, kind, data):
        output.write(struct.pack('>I', len(data)) + kind + data)
        output.write(struct.pack('>I', binascii.crc32(kind + data) & 0xffffffff))
    compressor = zlib.compressobj()
    with path.open('xb') as output:
        output.write(b'\x89PNG\r\n\x1a\n')
        chunk(output, b'IHDR', struct.pack('>IIBBBBB', width, height, 8, 2, 0, 0, 0))
        for y in range(height):
            pixels = row(y)
            if len(pixels) != width * 3:
                raise ValueError('PNG row length mismatch')
            data = compressor.compress(b'\0' + pixels)
            if data:
                chunk(output, b'IDAT', data)
        chunk(output, b'IDAT', compressor.flush())
        chunk(output, b'IEND', b'')


def numbered_rows(width, height, number):
    """编号与横向分段便于肉眼核对页框/半页/长图位置，不模拟照片性能。"""
    text = '{:03d}'.format(number)
    unit = max(8, min(width // 16, height // 12))
    left = (width - len(text) * 4 * unit) // 2
    top = max(16, min(height // 3, 200))
    base = ((number * 53) % 160 + 60, (number * 79) % 160 + 60, (number * 97) % 160 + 60)
    cache = {}
    def row(y):
        key = (min(4, y * 5 // height), (y - top) // unit if top <= y < top + 5 * unit else -1)
        if key in cache:
            return cache[key]
        shade = key[0] * 8
        pixels = bytearray(bytes(max(0, channel - shade) for channel in base) * width)
        # 左红/右蓝窄条帮助识别裁剪和阅读方向。
        pixels[:12 * 3] = b'\xff\x00\x00' * 12
        pixels[-12 * 3:] = b'\x00\x00\xff' * 12
        if key[1] >= 0:
            for index, digit in enumerate(text):
                for column, bit in enumerate(DIGITS[int(digit)][key[1]]):
                    if bit == '1':
                        x = left + (index * 4 + column) * unit
                        pixels[x * 3:(x + unit) * 3] = b'\xff\xff\xff' * unit
        cache[key] = bytes(pixels)
        return cache[key]
    return row


def main():
    """要求全新输出目录，生成夹具和SHA256清单，不覆盖既有文件。"""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('output', type=Path, help='尚不存在的夹具目录')
    args = parser.parse_args()
    root = args.output.resolve()
    root.mkdir(parents=True, exist_ok=False)
    reading = root / '01-reading'; reading.mkdir()
    retina = root / '02-retina'; retina.mkdir()
    stress = root / '03-memory'; stress.mkdir()
    records = []
    def add(folder, name, width, height, row, role):
        path = folder / name; write_png(path, width, height, row)
        records.append({'path': path.relative_to(root).as_posix(), 'width': width, 'height': height,
                        'role': role, 'sha256': hashlib.sha256(path.read_bytes()).hexdigest()})
    sizes = [(600, 900), (600, 900), (1600, 900), (600, 900), (900, 4500),
             (1000, 1000), (600, 900), (1200, 800), (600, 900)]
    for number, (width, height) in enumerate(sizes, 1):
        add(reading, '{:03d}.png'.format(number), width, height,
            numbered_rows(width, height, number), 'numbered reading page')
    for cell in (1, 2, 8):
        rows = [bytes(channel for x in range(800) for channel in
                      ((255,) * 3 if ((x // cell) + phase) % 2 else (0,) * 3)) for phase in (0, 1)]
        add(retina, 'checker-{}px-800x600.png'.format(cell), 800, 600,
            lambda y, rows=rows, cell=cell: rows[(y // cell) % 2], 'device pixel mapping')
    for number in range(1, 13):
        add(stress, '{:03d}.png'.format(number), 3840, 2160,
            numbered_rows(3840, 2160, number), 'synthetic 4K memory workload')
    for folder in (reading, stress):
        with zipfile.ZipFile(root / (folder.name + '.cbz'), 'x', zipfile.ZIP_DEFLATED) as archive:
            for path in sorted(folder.glob('*.png')):
                archive.write(path, path.name)
    (root / 'WindowsProfile').mkdir()
    (root / 'manifest.json').write_text(json.dumps({'scope': 'synthetic correctness/memory fixtures; not photo/format/screen performance proof',
        'pages': records}, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(root)
    print('{} PNGs, reading/memory CBZ and SHA256 manifest'.format(len(records)))


if __name__ == '__main__':
    main()
