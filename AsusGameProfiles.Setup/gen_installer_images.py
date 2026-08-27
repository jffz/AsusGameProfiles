from PIL import Image, ImageDraw, ImageFilter, ImageChops
import math

MATCHA_GLOW = (176, 224, 104)  # brighter neon-matcha, glow/mesh use only -- not a UI color
HONEY = (227, 176, 75)         # #E3B04B, matches AsusGameProfiles/Themes/Dark.xaml's retired AccentColor2 (kept for icon/badge art)
TEAL_DEEP = (40, 92, 84)       # deep teal-green, screen glass shading only
DARK1 = (11, 16, 11)
DARK2 = (19, 28, 19)
WHITE = (255, 255, 255)
LIGHT = (246, 247, 249)


def lerp(a, b, t):
    return a + (b - a) * t


def lerp_color(c1, c2, t):
    return tuple(int(lerp(c1[i], c2[i], t)) for i in range(3))


def diagonal_gradient(w, h, c1, c2, angle=45):
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
    """Meme badge que Assets/gen_icon.py (redessine ici a la taille demandee -- code duplique
    deliberement, pas une dependance partagee). 2026-08-27 redesign: dark matcha/honey aurora-mesh
    squircle badge with a glassy claymorphic monitor-screen glyph (no stand) and a glossy honey
    'switch' dot accent, replacing the earlier angular gaming-badge look."""
    s = size

    base = diagonal_gradient(s, s, DARK1, DARK2, angle=45)

    glow = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    glow = Image.alpha_composite(glow, blob(s, MATCHA_GLOW, s * 0.08, s * 0.06, s * 0.62, 140, s * 0.22))
    glow = Image.alpha_composite(glow, blob(s, HONEY, s * 0.97, s * 0.97, s * 0.42, 100, s * 0.16))
    base = Image.alpha_composite(base, glow)

    dome = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    dome = Image.alpha_composite(dome, blob(s, (255, 255, 255), s * 0.22, s * 0.16, s * 0.5, 35, s * 0.28))
    dome = Image.alpha_composite(dome, blob(s, (0, 0, 0), s * 0.82, s * 0.86, s * 0.5, 55, s * 0.26))
    base = Image.alpha_composite(base, dome)

    mask = squircle_mask(s, s, exponent=4.6, inset=s * 0.025)
    badge_img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    badge_img.paste(base, (0, 0), mask)

    inner_mask = squircle_mask(s, s, exponent=4.6, inset=s * 0.025 + s * 0.012)
    ring = ImageChops.subtract(mask, inner_mask)
    rim = Image.new("RGBA", (s, s), (255, 255, 255, 0))
    rim.putalpha(ImageChops.multiply(ring, Image.new("L", (s, s), 70)))
    badge_img = Image.alpha_composite(badge_img, rim)

    scr_w, scr_h = s * 0.56, s * 0.37
    scr_x, scr_y = (s - scr_w) / 2, s * 0.29
    scr_exp = 4.2
    scr_mask = squircle_mask(scr_w, scr_h, scr_exp)

    shadow_shape = Image.new("RGBA", (int(scr_w), int(scr_h)), (0, 0, 0, 0))
    shadow_shape.putalpha(scr_mask.point(lambda a: int(a * 0.55)))
    shadow_full = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    shadow_full.paste(shadow_shape, (int(scr_x + s * 0.018), int(scr_y + s * 0.028)), shadow_shape)
    shadow_full = shadow_full.filter(ImageFilter.GaussianBlur(s * 0.02))
    badge_img = Image.alpha_composite(badge_img, shadow_full)

    screen_fill = diagonal_gradient(scr_w, scr_h, MATCHA_GLOW, TEAL_DEEP, angle=35)
    scr_rgba = Image.new("RGBA", (int(scr_w), int(scr_h)), (0, 0, 0, 0))
    scr_rgba.paste(screen_fill, (0, 0))
    scr_rgba.putalpha(scr_mask)
    screen_layer = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    screen_layer.paste(scr_rgba, (int(scr_x), int(scr_y)), scr_rgba)
    badge_img = Image.alpha_composite(badge_img, screen_layer)

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

    dot_r = s * 0.10
    dcx, dcy = scr_x + scr_w - s * 0.045, scr_y + scr_h - s * 0.015

    dot_shadow = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    dd = ImageDraw.Draw(dot_shadow)
    dd.ellipse([dcx - dot_r, dcy - dot_r + s * 0.02, dcx + dot_r, dcy + dot_r + s * 0.02], fill=(0, 0, 0, 130))
    dot_shadow = dot_shadow.filter(ImageFilter.GaussianBlur(s * 0.018))
    badge_img = Image.alpha_composite(badge_img, dot_shadow)

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

    hi_r = dot_r * 0.30
    hcx, hcy = dcx - dot_r * 0.35, dcy - dot_r * 0.40
    hi_layer = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    hd = ImageDraw.Draw(hi_layer)
    hd.ellipse([hcx - hi_r, hcy - hi_r, hcx + hi_r, hcy + hi_r], fill=(255, 255, 240, 200))
    hi_layer = hi_layer.filter(ImageFilter.GaussianBlur(hi_r * 0.35))
    badge_img = Image.alpha_composite(badge_img, hi_layer)

    return badge_img


# ASUS Display Control's classic WixUI_InstallDir dialog set draws its OWN title/body text (black,
# fixed style) directly on top of these bitmaps, at positions this script doesn't control and that
# don't match a fixed 493x58 / 493x312 source (WiX stretches the art to whatever the real dialog size
# ends up being on this machine, which is larger and a different aspect ratio -- confirmed by testing
# the actual installer, not assumed). A dark background made that text unreadable everywhere it
# overlapped. Since exact overlap can't be predicted reliably, the only robust fix is keeping the
# ENTIRE canvas light enough for default black text to stay legible regardless of where it lands, and
# confining brand color to edges/corners that are least likely to matter.

# ===== WixUIBannerBmp: 493x58, shown atop every wizard page except Welcome/Finish =====
BANNER_W, BANNER_H = 493, 58
banner = Image.new("RGB", (BANNER_W, BANNER_H), LIGHT)

badge_small = make_badge(160).resize((40, 40), Image.LANCZOS)
banner.paste(badge_small, (BANNER_W - 40 - 10, (BANNER_H - 40) // 2), badge_small)

# thin matcha->honey accent line along the bottom edge only -- decorative, doesn't compete with WiX's own text
accent = diagonal_gradient(BANNER_W, 3, MATCHA_GLOW, HONEY).convert("RGB")
banner.paste(accent, (0, BANNER_H - 3))

banner.save("Banner.bmp")

# ===== WixUIDialogBmp: 493x312, full background of Welcome/Finish pages =====
DIALOG_W, DIALOG_H = 493, 312
dialog = Image.new("RGB", (DIALOG_W, DIALOG_H), WHITE)

# narrow colored stripe down the left edge only, rest of the canvas stays plain white
STRIPE_W = 54
stripe = diagonal_gradient(STRIPE_W, DIALOG_H, MATCHA_GLOW, HONEY).convert("RGB")
dialog.paste(stripe, (0, 0))

# small badge tucked in the bottom-right corner, well clear of where title/body text renders
badge_small2 = make_badge(240).resize((72, 72), Image.LANCZOS)
dialog.paste(badge_small2, (DIALOG_W - 72 - 16, DIALOG_H - 72 - 16), badge_small2)

dialog.save("Dialog.bmp")

print("done")
