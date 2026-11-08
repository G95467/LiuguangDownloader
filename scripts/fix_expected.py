import re
from pathlib import Path

path = Path("tests/LumenDownloader.Smoke/Program.cs")
text = path.read_text(encoding="utf-8")
expected = "\u6d4b\u8bd5\u62a5\u544a 2026.bin"
text, count = re.subn(
    r'var expectedName = "[^"\r\n]*";',
    f'var expectedName = "{expected}";',
    text,
)
path.write_text(text, encoding="utf-8", newline="\n")
print("replacements:", count)
print("expected:", expected)
