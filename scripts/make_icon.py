"""Generate the Lumen Downloader app icon (PNG + multi-size ICO)."""
from __future__ import annotations

import math
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parent.parent
ASSETS = ROOT / "assets"
ASSETS.mkdir(parents=True, exist_ok=True)

S = 2048
OUT = 1024
SCALE = S / 1024.0


def lerp(a, b, t):
    return a + (b - a) * t


def rounded_mask(size: int, radius: int) -> Image.Image:
    mask = Image.new("L", (size, size), 0)
    d = ImageDraw.Draw(mask)
    d.rounded_rectangle((0, 0, size - 1, size - 1), radius=radius, fill=255)
    return mask


def radial_glow(size: int, cx: float, cy: float, radius: float, color, strength: float) -> Image.Image:
    yy, xx = np.mgrid[0:size, 0:size]
    dist = np.sqrt((xx - cx) ** 2 + (yy - cy) ** 2)
    falloff = np.clip(1.0 - dist / radius, 0.0, 1.0) ** 2.2
    alpha = (falloff * strength * 255).astype(np.uint8)
    layer = Image.new("RGBA", (size, size), color + (0,))
    layer.putalpha(Image.fromarray(alpha, "L"))
    return layer


def gradient_layer(size: int, c1, c2, c3, angle: float) -> Image.Image:
    yy, xx = np.mgrid[0:size, 0:size]
    nx = xx / (size - 1)
    ny = yy / (size - 1)
    t = np.clip(nx * math.cos(angle) + ny * math.sin(angle), 0.0, 1.0)
    t = t ** 0.9

    arr = np.zeros((size, size, 3), dtype=np.float32)
    for i in range(3):
        arr[..., i] = np.where(
            t < 0.55,
            lerp(c1[i], c2[i], np.clip(t / 0.55, 0, 1)),
            lerp(c2[i], c3[i], np.clip((t - 0.55) / 0.45, 0, 1)),
        )
    return Image.fromarray(arr.astype(np.uint8), "RGB").convert("RGBA")


# ---- Background -----------------------------------------------------------
mask = rounded_mask(S, int(226 * SCALE))
bg = gradient_layer(S, (8, 12, 24), (14, 24, 48), (20, 16, 52), math.radians(38))
bg.putalpha(mask)

# Colour bloom.
glow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
glow = Image.alpha_composite(glow, radial_glow(S, S * 0.76, S * 0.17, S * 0.52, (45, 212, 255), 0.24))
glow = Image.alpha_composite(glow, radial_glow(S, S * 0.22, S * 0.85, S * 0.55, (124, 92, 255), 0.22))
glow.putalpha(Image.composite(glow.getchannel("A"), Image.new("L", (S, S), 0), mask))
bg = Image.alpha_composite(bg, glow)

# Fine noise for a tactile, premium surface.
rng = np.random.default_rng(20261107)
noise = rng.normal(0, 3.2, (S, S, 1)).repeat(3, axis=2)
surface = np.asarray(bg.convert("RGB"), dtype=np.float32) + noise
surface = np.clip(surface, 0, 255).astype(np.uint8)
bg = Image.fromarray(surface, "RGB").convert("RGBA")
bg.putalpha(mask)

# Soft inner rim.
rim = Image.new("RGBA", (S, S), (0, 0, 0, 0))
rd = ImageDraw.Draw(rim)
rd.rounded_rectangle(
    (int(7 * SCALE), int(7 * SCALE), S - int(7 * SCALE), S - int(7 * SCALE)),
    radius=int(220 * SCALE),
    outline=(255, 255, 255, 43),
    width=int(4 * SCALE),
)
inner = Image.new("RGBA", (S, S), (0, 0, 0, 0))
ImageDraw.Draw(inner).rounded_rectangle(
    (int(28 * SCALE), int(28 * SCALE), S - int(28 * SCALE), S - int(28 * SCALE)),
    radius=int(202 * SCALE),
    outline=(130, 200, 255, 20),
    width=int(2 * SCALE),
)

# Top glass sheen.
sheen = gradient_layer(S, (255, 255, 255), (150, 220, 255), (255, 255, 255), math.radians(105))
sheen_alpha = np.linspace(56, 0, S).astype(np.uint8)[:, None].repeat(S, axis=1)
sheen_alpha = np.clip(sheen_alpha * 0.55, 0, 255).astype(np.uint8)
sheen.putalpha(Image.fromarray(sheen_alpha, "L"))
sheen.putalpha(Image.composite(sheen.getchannel("A"), Image.new("L", (S, S), 0), mask))
bg = Image.alpha_composite(bg, sheen)
bg = Image.alpha_composite(bg, rim)
bg = Image.alpha_composite(bg, inner)


def p(v: float) -> float:
    return v * SCALE


# ---- Symbol ---------------------------------------------------------------
# Glow behind the arrow.
mark_glow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
mg = ImageDraw.Draw(mark_glow)
mg.rounded_rectangle((p(410), p(244), p(614), p(640)), radius=p(82), fill=(65, 237, 255, 150))
mg.polygon(
    [(p(318), p(485)), (p(512), p(714)), (p(706), p(485))],
    fill=(120, 140, 255, 125),
)
mark_glow = mark_glow.filter(ImageFilter.GaussianBlur(p(58)))

symbol = Image.new("RGBA", (S, S), (0, 0, 0, 0))
sd = ImageDraw.Draw(symbol)

# Main down arrow built from a rounded stem + rounded chevron.
stem = Image.new("RGBA", (S, S), (0, 0, 0, 0))
sd_stem = ImageDraw.Draw(stem)
sd_stem.rounded_rectangle(
    (p(463), p(250), p(561), p(598)), radius=p(49), fill=(255, 255, 255, 255)
)
chev = Image.new("RGBA", (S, S), (0, 0, 0, 0))
sd_chev = ImageDraw.Draw(chev)
sd_chev.line(
    [(p(356), p(476)), (p(512), p(648)), (p(668), p(476))],
    fill=(255, 255, 255, 255),
    width=int(p(96)),
    joint="curve",
)
for center in [(p(356), p(476)), (p(668), p(476)), (p(512), p(648))]:
    r = p(48)
    sd_chev.ellipse(
        (center[0] - r, center[1] - r, center[0] + r, center[1] + r),
        fill=(255, 255, 255, 255),
    )

# Tint the white mark with a cool gradient, preserving alpha.
tint = gradient_layer(S, (255, 255, 255), (187, 247, 255), (166, 182, 255), math.radians(90))
combined = Image.alpha_composite(stem, chev)
combined.putalpha(Image.composite(combined.getchannel("A"), Image.new("L", (S, S), 0), mask))
tint.putalpha(combined.getchannel("A"))
symbol = Image.alpha_composite(symbol, tint)

# Tray / baseline: a luminous bar.
tray = Image.new("RGBA", (S, S), (0, 0, 0, 0))
td = ImageDraw.Draw(tray)
td.rounded_rectangle((p(332), p(734), p(692), p(782)), radius=p(24), fill=(179, 248, 255, 230))
td.rounded_rectangle((p(250), p(816), p(548), p(842)), radius=p(13), fill=(120, 232, 255, 130))
td.rounded_rectangle((p(592), p(816), p(774), p(842)), radius=p(13), fill=(148, 125, 255, 150))
tray = tray.filter(ImageFilter.GaussianBlur(p(0.7)))

# Compose with a restrained glow.
bg = Image.alpha_composite(bg, mark_glow)
bg = Image.alpha_composite(bg, symbol)
bg = Image.alpha_composite(bg, tray)

# Final soft bloom on the brightest edges.
bloom_src = bg.copy()
from PIL import ImageChops

bloom = bloom_src.filter(ImageFilter.GaussianBlur(p(13)))
bloom.putalpha(Image.eval(bloom.getchannel("A"), lambda v: int(v * 0.28)))
bg = Image.alpha_composite(bg, bloom)

# Crisp edge and transparent outside.
bg.putalpha(mask)

icon = bg.resize((OUT, OUT), Image.Resampling.LANCZOS)
icon.save(ASSETS / "icon.png", "PNG", optimize=True)
icon.resize((256, 256), Image.Resampling.LANCZOS).save(ASSETS / "icon-256.png", "PNG", optimize=True)
icon.resize((64, 64), Image.Resampling.LANCZOS).save(ASSETS / "icon-64.png", "PNG", optimize=True)
icon.resize((32, 32), Image.Resampling.LANCZOS).save(ASSETS / "icon-32.png", "PNG", optimize=True)
icon.save(
    ASSETS / "icon.ico",
    format="ICO",
    sizes=[(16, 16), (20, 20), (24, 24), (32, 32), (40, 40), (48, 48), (64, 64), (128, 128), (256, 256)],
)

print("Wrote", ASSETS / "icon.png")
print("Wrote", ASSETS / "icon.ico")
