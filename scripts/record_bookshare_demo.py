#!/usr/bin/env python3
"""Manually record BookShare in a 1920x1080 browser window and export an MP4.

Run from the repository root after starting the ASP.NET Core app:
    python scripts/record_bookshare_demo.py --url https://localhost:5001

The browser remains interactive so the operator can use real test accounts and
perform the real workflows. This script does not fabricate clicks or database results.
"""
from __future__ import annotations

import argparse
import shutil
import subprocess
import sys
import time
from pathlib import Path

try:
    from playwright.sync_api import sync_playwright
except ImportError as exc:  # pragma: no cover - depends on the operator's machine
    raise SystemExit(
        "Thiếu Playwright. Cài bằng: python -m pip install playwright && python -m playwright install chromium"
    ) from exc


ROOT = Path(__file__).resolve().parents[1]
DOCS = ROOT / "docs"
TEMP_DIR = DOCS / ".video_recording"
OUTPUT = DOCS / "BookShare_Video_Demo.mp4"
SUBTITLES = DOCS / "BookShare_Video_Demo.vi.srt"


def run() -> int:
    parser = argparse.ArgumentParser(description="Quay video demo BookShare ở độ phân giải 1080p.")
    parser.add_argument("--url", default="https://localhost:5001", help="URL ứng dụng BookShare đang chạy")
    args = parser.parse_args()

    ffmpeg = shutil.which("ffmpeg")
    if not ffmpeg:
        raise SystemExit("Không tìm thấy ffmpeg. Cài FFmpeg và chạy lại script.")

    DOCS.mkdir(parents=True, exist_ok=True)
    TEMP_DIR.mkdir(parents=True, exist_ok=True)
    for old_file in TEMP_DIR.glob("*.webm"):
        old_file.unlink()

    with sync_playwright() as playwright:
        browser = playwright.chromium.launch(headless=False, args=["--start-maximized"])
        context = browser.new_context(
            viewport={"width": 1920, "height": 1080},
            screen={"width": 1920, "height": 1080},
            device_scale_factor=1,
            ignore_https_errors=True,
            record_video_dir=str(TEMP_DIR),
            record_video_size={"width": 1920, "height": 1080},
        )
        page = context.new_page()
        page.goto(args.url, wait_until="domcontentloaded", timeout=120_000)
        page.bring_to_front()

        print("\nĐANG QUAY BOOKSHARE – 1920×1080")
        print("Hãy tự thao tác trên cửa sổ Chromium theo docs/BookShare_Video_Demo_Script.md.")
        print("Dùng dữ liệu kiểm thử; không để lộ mật khẩu, OTP thật, cookie hoặc token.")
        print("Thời lượng mục tiêu: 3–5 phút. Nhấn Enter tại terminal để dừng và xuất MP4.\n")
        started_at = time.monotonic()
        try:
            input("Đang quay... nhấn Enter sau khi hoàn tất luồng demo: ")
        except EOFError:
            print("Không nhận được thao tác kết thúc từ terminal.")
        elapsed = time.monotonic() - started_at

        video = page.video
        context.close()
        browser.close()

    source = Path(video.path()) if video else None
    if not source or not source.exists():
        raise SystemExit("Playwright không tạo được file video nguồn.")

    ffmpeg_args = [ffmpeg, "-y", "-i", str(source)]
    if SUBTITLES.exists():
        # Embed Vietnamese subtitles as an MP4 text track (mov_text).
        ffmpeg_args.extend(["-sub_charenc", "UTF-8", "-i", str(SUBTITLES)])

    ffmpeg_args.extend([
        "-map", "0:v:0",
        "-vf", "scale=1920:1080:force_original_aspect_ratio=decrease,pad=1920:1080:(ow-iw)/2:(oh-ih)/2,format=yuv420p",
        "-r", "30", "-c:v", "libx264", "-preset", "medium", "-crf", "20",
    ])
    if SUBTITLES.exists():
        ffmpeg_args.extend(["-map", "1:0", "-c:s", "mov_text", "-metadata:s:s:0", "language=vie"])
    ffmpeg_args.extend(["-movflags", "+faststart", str(OUTPUT)])

    result = subprocess.run(ffmpeg_args, check=False)
    if result.returncode != 0 or not OUTPUT.exists():
        raise SystemExit("FFmpeg không xuất được MP4. File WebM nguồn được giữ trong docs/.video_recording/.")

    minutes = elapsed / 60
    print(f"\nĐã xuất: {OUTPUT.relative_to(ROOT)}")
    print(f"Thời gian quay: {minutes:.1f} phút")
    if not 3 <= minutes <= 5:
        print("CẢNH BÁO: video chưa nằm trong khoảng 3–5 phút; hãy quay lại hoặc biên tập thời lượng.")
    print("Video có track phụ đề tiếng Việt (có thể bật/tắt trong trình phát). Script không thu âm giọng đọc.")
    return 0


if __name__ == "__main__":
    sys.exit(run())
