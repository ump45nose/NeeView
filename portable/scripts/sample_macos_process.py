#!/usr/bin/env python3
"""后台采集指定真实进程RSS/CPU/文件描述符；不启动应用或发送输入。"""
import argparse
import csv
from datetime import datetime, timezone
from pathlib import Path
import subprocess
import time


def process_sample(pid):
    """读取系统ps和lsof，进程退出返回None；RSS单位为bytes，缺失FD记空值。"""
    result = subprocess.run(['ps', '-p', str(pid), '-o', 'rss=,pcpu='], text=True, capture_output=True, timeout=5)
    fields = result.stdout.split()
    if result.returncode or len(fields) != 2:
        return None
    descriptors = subprocess.run(['/usr/sbin/lsof', '-a', '-p', str(pid), '-Ff'], text=True, capture_output=True, timeout=10)
    count = sum(line[1:].isdigit() for line in descriptors.stdout.splitlines() if line.startswith('f')) if descriptors.returncode == 0 else ''
    return int(fields[0]) * 1024, fields[1], count


def main():
    """只采集指定PID，默认每5秒持续30分钟；新建CSV，禁止覆盖旧证据。"""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--pid', type=int, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--interval', type=float, default=5)
    parser.add_argument('--duration', type=float, default=1800)
    args = parser.parse_args()
    if args.pid <= 0 or args.interval <= 0 or args.duration < 0:
        parser.error('pid/interval must be positive; duration must be nonnegative')
    if process_sample(args.pid) is None:
        parser.error('target process is not running')
    started = time.monotonic()
    with args.output.open('x', newline='', encoding='utf-8') as output:
        writer = csv.writer(output)
        writer.writerow(['utc', 'elapsed_seconds', 'pid', 'rss_bytes', 'cpu_percent', 'open_file_descriptors'])
        while True:
            sample = process_sample(args.pid)
            if sample is None:
                print('Target exited; evidence flushed.'); break
            elapsed = time.monotonic() - started
            writer.writerow([datetime.now(timezone.utc).isoformat(), round(elapsed, 3), args.pid, *sample])
            output.flush()
            if elapsed >= args.duration:
                break
            time.sleep(min(args.interval, args.duration - elapsed))
    print(args.output)


if __name__ == '__main__':
    main()
