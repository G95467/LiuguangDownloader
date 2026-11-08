import re
from pathlib import Path

path = Path("tests/LumenDownloader.Smoke/Program.cs")
text = path.read_text(encoding="utf-8")
# Restore the one string literal damaged by a previous PowerShell rewrite.
text, count = re.subn(
    r'ex\.Message\.Contains\("[^"\r\n]{1,8}\?, StringComparison\.Ordinal\)\);',
    'ex.Message.Contains("不存在", StringComparison.Ordinal));',
    text,
)
path.write_text(text, encoding="utf-8", newline="\n")
print("replacements:", count)
print(text[text.find("404 message") : text.find("404 message") + 180])
