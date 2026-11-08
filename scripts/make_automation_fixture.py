import json
from pathlib import Path

root = Path(r"C:\Users\Lenovo\Documents\deepseek-harness\default-workspace\.ui-test\automation")
downloads = root / "downloads"
root.mkdir(parents=True, exist_ok=True)
downloads.mkdir(parents=True, exist_ok=True)
state = {
    "version": 1,
    "settings": {
        "downloadDirectory": str(downloads),
        "connections": 8,
        "maxConcurrent": 2,
        "watchClipboard": False,
        "completionSound": False,
        "allowInvalidCertificate": False,
        "userAgent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36",
    },
    "tasks": [],
}
(root / "state.json").write_text(json.dumps(state, ensure_ascii=False, indent=2), encoding="utf-8")
print(state["settings"]["downloadDirectory"])
