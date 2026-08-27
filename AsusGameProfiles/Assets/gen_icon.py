from PIL import Image, ImageDraw, ImageFilter, ImageChops
import math

SIZE = 512
S = SIZE

MATCHA = (151, 194, 94)        # #97C25E, matches Themes/Dark.xaml AccentColor
MATCHA_GLOW = (176, 224, 104)  # brighter neon-matcha, glow/mesh use only -- not a UI color
HONEY = (227, 176, 75)         # #E3B04B, matches Themes/Dark.xaml AccentColor2 (retired as a UI gradient, kept for icon art)
TEAL_DEEP = (40, 92, 84)       # deep teal-green, screen glass shading only
DARK1 = (11, 16, 11)
DARK2 = (19, 28, 19)


def lerp(a, b, t):
    return a + (b - a) * t


def lerp_color(c1, c2, t):
    return tuple(int(lerp(c1[i], c2[i], t)) for i in range(3))


def diagonal_gradient(w, h, c1, c2, angle=45):
    """Smooth diagonal gradient via linear_gradient + rotate + crop (fast, no per-pixel loop,
    smoother/less banding than manual pixel math -- Pillow's own resample handles the AA)."""
    w, h = int(w), int(h)
    base = max(w, h) * 2
    grad = Image.linear_gradient("L").resize((base, base))
    grad = grad.rotate(angle, resample=Image.BICUBIC, expand=False)
    gw, gh = grad.size
    left = (gw - w) // 2
    top = (gh - h) // 2
    grad = grad.crop((left, top, left + w, top + h))
    c1img = Image.new("RGB", (w, h), c1)
    c2img = Image.new("RGB", (w, h), c2)
    return Image.composite(c2img, c1img, grad).convert("RGBA")


def squircle_mask(w, h, exponent, inset=0.0, ss=4):
    """Antialiased superellipse ('squircle') mask, w x h. exponent: 2 = ellipse, ~4.5 = squircle
    (iOS-style), higher = more rectangular corners. inset shrinks the shape inward by that many
    px on each side. Rendered as a polygon at 4x supersample then downsized for antialiasing --
    much cheaper and smoother than a per-pixel Python loop over the full-res canvas."""
    w, h = int(w), int(h)
    n = exponent
    steps = 300
    pts = []
    for i in range(steps):
        t = 2 * math.pi * i / steps
        ct, st = math.cos(t), math.sin(t)
        x = math.copysign(abs(ct) ** (2.0 / n), ct)
        y = math.copysign(abs(st) ** (2.0 / n), st)
        pts.append((x, y))
    rx, ry = w / 2 - inset, h / 2 - inset
    cx, cy = w / 2, h / 2
    big = Image.new("L", (w * ss, h * ss), 0)
    bd = ImageDraw.Draw(big)
    bigpoly = [(cx * ss + x * rx * ss, cy * ss + y * ry * ss) for x, y in pts]
    bd.polygon(bigpoly, fill=255)
    return big.resize((w, h), Image.LANCZOS)


def blob(size, color, cx, cy, radius, opacity, blur):
    layer = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    d.ellipse([cx - radius, cy - radius, cx + radius, cy + radius], fill=(*color, opacity))
    return layer.filter(ImageFilter.GaussianBlur(blur))


def make_badge(size):
    """2026-08-27 redesign (previous angular gaming-badge look was rejected as ugly). Direction:
    dark matcha/honey aurora-mesh squircle badge (soft superellipse, not a plain rounded-rect --
    closer to current iOS/Arc/Linear-style app icon shapes) with a glassy claymorphic monitor-
    screen glyph (drop shadow + specular highlight for a "sticker lifted off the badge" pop,
    rather than a flat gradient fill) and a glossy honey "switch" dot accent overlapping its
    corner. No stand/neck on the monitor glyph this time -- those thin details turned to mud at
    16px in the old design; dropping them is a deliberate legibility fix, not an oversight."""
    s = size

    # ---- background: dark diagonal base + two-color aurora mesh (matcha top-left, honey corner) ----
    base = diagonal_gradient(s, s, DARK1, DARK2, angle=45)

    glow = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    glow = Image.alpha_composite(glow, blob(s, MATCHA_GLOW, s * 0.08, s * 0.06, s * 0.62, 140, s * 0.22))
    glow = Image.alpha_composite(glow, blob(s, HONEY, s * 0.97, s * 0.97, s * 0.42, 100, s * 0.16))
    base = Image.alpha_composite(base, glow)

    # subtle clay dimensionality: soft inner highlight top-left, soft inner shadow bottom-right
    dome = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    dome = Image.alpha_composite(dome, blob(s, (255, 255, 255), s * 0.22, s * 0.16, s * 0.5, 35, s * 0.28))
    dome = Image.alpha_composite(dome, blob(s, (0, 0, 0), s * 0.82, s * 0.86, s * 0.5, 55, s * 0.26))
    base = Image.alpha_composite(base, dome)

    # ---- squircle badge mask ----
    mask = squircle_mask(s, s, exponent=4.6, inset=s * 0.025)
    badge_img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    badge_img.paste(base, (0, 0), mask)

    # thin bright rim stroke (ring = outer mask minus a slightly-inset inner mask)
    inner_mask = squircle_mask(s, s, exponent=4.6, inset=s * 0.025 + s * 0.012)
    ring = ImageChops.subtract(mask, inner_mask)
    rim = Image.new("RGBA", (s, s), (255, 255, 255, 0))
    rim.putalpha(ImageChops.multiply(ring, Image.new("L", (s, s), 70)))
    badge_img = Image.alpha_composite(badge_img, rim)

    # ================= glyph: glossy "screen" squircle (no stand -- reads clean at 16px) =================
    scr_w, scr_h = s * 0.56, s * 0.37
    scr_x, scr_y = (s - scr_w) / 2, s * 0.29
    scr_exp = 4.2
    scr_mask = squircle_mask(scr_w, scr_h, scr_exp)

    # drop shadow of the screen glyph, offset down-right, blurred -- "clay sticker lifted off surface"
    shadow_shape = Image.new("RGBA", (int(scr_w), int(scr_h)), (0, 0, 0, 0))
    shadow_shape.putalpha(scr_mask.point(lambda a: int(a * 0.55)))
    shadow_full = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    shadow_full.paste(shadow_shape, (int(scr_x + s * 0.018), int(scr_y + s * 0.028)), shadow_shape)
    shadow_full = shadow_full.filter(ImageFilter.GaussianBlur(s * 0.02))
    badge_img = Image.alpha_composite(badge_img, shadow_full)

    # glass fill: vivid matcha -> deep teal diagonal gradient (richer/more saturated than a pastel wash)
    screen_fill = diagonal_gradient(scr_w, scr_h, MATCHA_GLOW, TEAL_DEEP, angle=35)
    scr_rgba = Image.new("RGBA", (int(scr_w), int(scr_h)), (0, 0, 0, 0))
    scr_rgba.paste(screen_fill, (0, 0))
    scr_rgba.putalpha(scr_mask)
    screen_layer = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    screen_layer.paste(scr_rgba, (int(scr_x), int(scr_y)), scr_rgba)
    badge_img = Image.alpha_composite(badge_img, screen_layer)

    # specular highlight streak (diagonal soft white band across upper screen -- glass reflection)
    spec = Image.new("L", (int(scr_w), int(scr_h)), 0)
    sd = ImageDraw.Draw(spec)
    sd.polygon([
        (0, scr_h * 0.05), (scr_w * 0.55, scr_h * 0.05),
        (scr_w * 0.30, scr_h * 0.55), (0, scr_h * 0.55),
    ], fill=140)
    spec = spec.filter(ImageFilter.GaussianBlur(scr_h * 0.08))
    spec_rgba = Image.new("RGBA", (int(scr_w), int(scr_h)), (255, 255, 255, 0))
    spec_rgba.putalpha(ImageChops.multiply(spec, scr_mask))
    screen_layer2 = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    screen_layer2.paste(spec_rgba, (int(scr_x), int(scr_y)), spec_rgba)
    badge_img = Image.alpha_composite(badge_img, screen_layer2)

    # thin light bezel frame inset within the screen (reads as "monitor" cue at larger sizes)
    bezel_inset = scr_w * 0.10
    bez_w, bez_h = scr_w - 2 * bezel_inset, scr_h - 2 * bezel_inset
    if bez_w > 4 and bez_h > 4:
        bez_outer = squircle_mask(bez_w, bez_h, scr_exp)
        bez_inner = squircle_mask(bez_w, bez_h, scr_exp, inset=min(bez_w, bez_h) * 0.09)
        bez_ring = ImageChops.subtract(bez_outer, bez_inner)
        ring_rgba = Image.new("RGBA", (int(bez_w), int(bez_h)), (255, 255, 255, 0))
        ring_rgba.putalpha(bez_ring.point(lambda a: int(a * 0.55)))
        bez_layer = Image.new("RGBA", (s, s), (255, 255, 255, 0))
        bez_layer.paste(ring_rgba, (int(scr_x + bezel_inset), int(scr_y + bezel_inset)), ring_rgba)
        badge_img = Image.alpha_composite(badge_img, bez_layer)

    # ================= accent: glossy honey "switch" dot, bottom-right overlap =================
    dot_r = s * 0.10
    dcx, dcy = scr_x + scr_w - s * 0.045, scr_y + scr_h - s * 0.015

    dot_shadow = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    dd = ImageDraw.Draw(dot_shadow)
    dd.ellipse([dcx - dot_r, dcy - dot_r + s * 0.02, dcx + dot_r, dcy + dot_r + s * 0.02], fill=(0, 0, 0, 130))
    dot_shadow = dot_shadow.filter(ImageFilter.GaussianBlur(s * 0.018))
    badge_img = Image.alpha_composite(badge_img, dot_shadow)

    # glossy sphere fill via radial gradient (light honey center -> deep honey edge)
    grad_size = max(2, int(dot_r * 2))
    rg = Image.radial_gradient("L").resize((grad_size, grad_size))
    light_honey = lerp_color(HONEY, (255, 250, 225), 0.55)
    deep_honey = lerp_color(HONEY, (120, 70, 20), 0.35)
    c1img = Image.new("RGB", (grad_size, grad_size), light_honey)
    c2img = Image.new("RGB", (grad_size, grad_size), deep_honey)
    sphere = Image.composite(c2img, c1img, rg).convert("RGBA")
    circle_mask = Image.new("L", (grad_size, grad_size), 0)
    ImageDraw.Draw(circle_mask).ellipse([0, 0, grad_size, grad_size], fill=255)
    sphere.putalpha(circle_mask)
    dot_layer = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    dot_layer.paste(sphere, (int(dcx - dot_r), int(dcy - dot_r)), sphere)
    badge_img = Image.alpha_composite(badge_img, dot_layer)

    # tiny specular highlight on the dot
    hi_r = dot_r * 0.30
    hcx, hcy = dcx - dot_r * 0.35, dcy - dot_r * 0.40
    hi_layer = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    hd = ImageDraw.Draw(hi_layer)
    hd.ellipse([hcx - hi_r, hcy - hi_r, hcx + hi_r, hcy + hi_r], fill=(255, 255, 240, 200))
    hi_layer = hi_layer.filter(ImageFilter.GaussianBlur(hi_r * 0.35))
    badge_img = Image.alpha_composite(badge_img, hi_layer)

    return badge_img


img = make_badge(S)
img.save("icon_512.png")

sizes = [16, 24, 32, 48, 64, 128, 256]
img.save(
    "AppIcon_new.ico",
    format="ICO",
    sizes=[(s, s) for s in sizes],
)

# Standalone PNG for XAML <Image> placements bigger than a taskbar/title-bar glyph (e.g. the app
# banner). A plain <Image Source="AppIcon.ico"> lets WPF pick whichever frame it decides is the
# "default" -- in practice that's frame 0 (16x16, the first entry in `sizes` above), stretched up
# to whatever size the Image control renders at, which looks blurry/blocky at anything past ~20px.
# The .ico stays multi-resolution for Window.Icon/taskbar/Alt-Tab, where Windows itself picks the
# right frame; this PNG is for anywhere this project's own XAML displays the icon large.
img.resize((256, 256), Image.LANCZOS).save("AppIcon_new.png")
print("done")
