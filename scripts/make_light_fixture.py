import json
import sys
from pathlib import Path
from datetime import datetime, timedelta, timezone

default_root = r"C:\Users\Lenovo\Documents\deepseek-harness\default-workspace\.ui-test\light"
root = Path(sys.argv[1] if len(sys.argv) > 1 else default_root)
downloads = root / "downloads"
downloads.mkdir(parents=True, exist_ok=True)
now = datetime.now(timezone(timedelta(hours=8))).isoformat()

def make_task(**kwargs):
    base = {
        "id": "",
        "url": "",
        "host": "",
        "fileName": "",
        "saveDirectory": str(downloads),
        "finalPath": None,
        "partPath": None,
        "resolvedUrl": None,
        "mimeType": None,
        "error": None,
        "status": "paused",
        "totalBytes": 0,
        "downloadedBytes": 0,
        "chunkSize": 0,
        "requestedConnections": 16,
        "activeConnections": 0,
        "totalSegments": 0,
        "completedSegments": 0,
        "supportsRanges": True,
        "isResumable": True,
        "etag": None,
        "lastModified": None,
        "completedChunks": [],
        "referer": None,
        "cookie": None,
        "customHeaders": None,
        "allowInvalidCertificate": False,
        "createdAt": now,
        "completedAt": None,
        "speedBytesPerSecond": 0,
        "etaSeconds": None,
    }
    base.update(kwargs)
    return base

paused_total = 1342177280
paused_chunk = 16 * 1024 * 1024
paused_segments = (paused_total + paused_chunk - 1) // paused_chunk
paused_chunks = list(range(30))

error_total = 29779558
error_chunk = 1024 * 1024
error_segments = (error_total + error_chunk - 1) // error_chunk
error_chunks = list(range(3))

completed_total = 175321088

tasks = [
    make_task(
        id="light-completed-001",
        url="https://downloads.example.com/packs/aurora-materials.zip",
        host="downloads.example.com",
        fileName="光影素材包.zip",
        status="completed",
        totalBytes=completed_total,
        downloadedBytes=completed_total,
        chunkSize=8 * 1024 * 1024,
        requestedConnections=16,
        totalSegments=21,
        completedSegments=21,
        supportsRanges=True,
        isResumable=False,
        completedChunks=list(range(21)),
        finalPath=str(downloads / "光影素材包.zip"),
        completedAt=(datetime.now(timezone(timedelta(hours=8))) - timedelta(minutes=24)).isoformat(),
        createdAt=(datetime.now(timezone(timedelta(hours=8))) - timedelta(hours=1)).isoformat(),
    ),
    make_task(
        id="light-paused-002",
        url="https://cdn.example.com/video/travel-documentary-4k.mp4",
        host="cdn.example.com",
        fileName="旅行纪录片 4K.mp4",
        status="paused",
        totalBytes=paused_total,
        downloadedBytes=480 * 1024 * 1024,
        chunkSize=paused_chunk,
        requestedConnections=16,
        totalSegments=paused_segments,
        completedSegments=len(paused_chunks),
        supportsRanges=True,
        completedChunks=paused_chunks,
        partPath=str(downloads / "旅行纪录片 4K.mp4.ee44aa11.lgpart"),
        createdAt=(datetime.now(timezone(timedelta(hours=8))) - timedelta(minutes=2)).isoformat(),
    ),
    make_task(
        id="light-error-003",
        url="https://files.example.com/design/annual-report.pdf",
        host="files.example.com",
        fileName="年度设计报告 2026.pdf",
        status="error",
        error="服务器拒绝访问，链接可能已过期（HTTP 403）",
        totalBytes=error_total,
        downloadedBytes=3 * 1024 * 1024,
        chunkSize=error_chunk,
        requestedConnections=8,
        totalSegments=error_segments,
        completedSegments=len(error_chunks),
        supportsRanges=True,
        completedChunks=error_chunks,
        partPath=str(downloads / "年度设计报告 2026.pdf.aa22bb33.lgpart"),
        createdAt=(datetime.now(timezone(timedelta(hours=8))) - timedelta(minutes=12)).isoformat(),
    ),
]

state = {
    "version": 1,
    "settings": {
        "downloadDirectory": str(downloads),
        "connections": 16,
        "maxConcurrent": 3,
        "watchClipboard": False,
        "completionSound": False,
        "allowInvalidCertificate": False,
        "userAgent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36",
    },
    "tasks": tasks,
}
(root / "state.json").write_text(json.dumps(state, ensure_ascii=False, indent=2), encoding="utf-8")
print(root / "state.json")
print("paused segments", paused_segments, "error segments", error_segments)
