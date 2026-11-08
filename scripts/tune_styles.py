from pathlib import Path
import re

path = Path("src/LumenDownloader.App/ui/styles.css")
text = path.read_text(encoding="utf-8")

pairs = [
    ("--text-2: #98a5ba;", "--text-2: #a9b6c9;"),
    ("--text-3: #627086;", "--text-3: #7b899f;"),
    ("--text-4: #48556a;", "--text-4: #59677c;"),
    ("--line: rgba(183, 207, 255, 0.075);", "--line: rgba(183, 207, 255, 0.09);"),
    ("--line-strong: rgba(183, 207, 255, 0.15);", "--line-strong: rgba(183, 207, 255, 0.17);"),
    ("--line-faint: rgba(183, 207, 255, 0.045);", "--line-faint: rgba(183, 207, 255, 0.055);"),
    ("--text-2: #5d6b81;", "--text-2: #56657b;"),
    ("--text-3: #8a96aa;", "--text-3: #77859b;"),
    ("--text-4: #a8b2c2;", "--text-4: #919eb1;"),
    ("min-height: 98px;", "min-height: 102px;"),
    (
        ".task-actions {\n  display: flex;\n  align-items: center;\n  gap: 2px;\n  flex: none;\n  margin-left: auto;\n  opacity: 0.55;",
        ".task-actions {\n  display: flex;\n  align-items: center;\n  gap: 2px;\n  flex: none;\n  margin-left: auto;\n  opacity: 0.75;",
    ),
    (
        ".task-sub {\n  display: flex;\n  align-items: center;\n  gap: 5px;\n  min-width: 0;\n  margin-top: 2px;\n  color: var(--text-4);",
        ".task-sub {\n  display: flex;\n  align-items: center;\n  gap: 5px;\n  min-width: 0;\n  margin-top: 2px;\n  color: var(--text-3);",
    ),
    (
        ".task-host {\n  display: inline-flex;\n  align-items: center;\n  gap: 4px;\n  max-width: 185px;\n  overflow: hidden;\n  color: var(--text-3);",
        ".task-host {\n  display: inline-flex;\n  align-items: center;\n  gap: 4px;\n  max-width: 185px;\n  overflow: hidden;\n  color: var(--text-2);",
    ),
    (
        ".task-meta {\n  display: flex;\n  align-items: center;\n  gap: 5px;\n  min-height: 17px;\n  margin-top: 3px;\n  color: var(--text-4);",
        ".task-meta {\n  display: flex;\n  align-items: center;\n  gap: 5px;\n  min-height: 18px;\n  margin-top: 3px;\n  color: var(--text-3);",
    ),
    (".quick-actions .icon-button {\n  width: 36px;\n  height: 36px;", ".quick-actions .icon-button {\n  width: 38px;\n  height: 38px;"),
    (".quick-actions .soft-button span", ".quick-actions .soft-button span"),
    (
        ".soft-button {\n  display: flex;\n  align-items: center;\n  gap: 6px;\n  height: 36px;",
        ".soft-button {\n  display: flex;\n  align-items: center;\n  gap: 6px;\n  height: 38px;",
    ),
    (
        ".download-button {\n  position: relative;\n  display: inline-flex;\n  align-items: center;\n  justify-content: center;\n  gap: 7px;\n  height: 38px;",
        ".download-button {\n  position: relative;\n  display: inline-flex;\n  align-items: center;\n  justify-content: center;\n  gap: 7px;\n  height: 40px;",
    ),
    (".download-button:disabled {\n  cursor: not-allowed;\n  opacity: 0.4;", ".download-button:disabled {\n  cursor: not-allowed;\n  opacity: 0.52;"),
    (".threads-pill {\n  display: inline-flex;\n  align-items: center;\n  gap: 3px;\n  height: 17px;", ".threads-pill {\n  display: inline-flex;\n  align-items: center;\n  gap: 3px;\n  height: 18px;"),
]

for old, new in pairs:
    if old == new:
        continue
    count = text.count(old)
    if count == 0:
        print("MISS:", old.splitlines()[0])
    text = text.replace(old, new)

font_map = {
    "7": "8",
    "7.5": "8.5",
    "8": "9",
    "8.5": "9.5",
    "9": "10",
    "9.5": "10.5",
    "10": "10.5",
    "10.5": "11",
    "11": "11.5",
    "11.5": "12",
    "12": "12.5",
    "12.5": "13",
    "13": "13.5",
    "14.5": "15",
}


def bump(match: re.Match[str]) -> str:
    value = match.group(1)
    return f"font-size: {font_map.get(value, value)}px;"


text = re.sub(r"font-size: (\d+(?:\.\d+)?)px;", bump, text)
path.write_text(text, encoding="utf-8")
print("updated", path)
