"""
Generate application icon for AZW3 Reader
Creates a professional book icon as PNG and ICO files
"""

from PIL import Image, ImageDraw, ImageFilter
import os

OUTPUT_DIR = os.path.dirname(os.path.abspath(__file__))


def create_base_icon(size=512):
    """Create the icon at the given size."""
    img = Image.new('RGBA', (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)

    S = size / 512.0  # Scale factor
    def s(v): return int(v * S)

    # Colors
    BG_DARK = (44, 95, 138)
    BG_MID = (30, 74, 112)
    GOLD = (232, 168, 56)
    GOLD_DARK = (212, 146, 42)
    PAGE_L = (245, 240, 232)
    PAGE_R = (255, 255, 255)
    PAGE_BOTTOM = (216, 208, 192)
    TEXT_CLR = (180, 170, 155)

    cx, cy = s(256), s(256)

    # === Background rounded square ===
    # Draw background in vertical bands for gradient effect
    for i in range(256):
        t = i / 255.0
        r = int(BG_DARK[0] * (1 - t * 0.6) + BG_MID[0] * (t * 0.6))
        g = int(BG_DARK[1] * (1 - t * 0.6) + BG_MID[1] * (t * 0.6))
        b = int(BG_DARK[2] * (1 - t * 0.8) + BG_MID[2] * (t * 0.8))
        y0 = s(16) + int(i * s(480) / 256)
        y1 = y0 + max(1, s(2))
        draw.rectangle([s(16), y0, s(496), y1], fill=(r, g, b))

    # Rounded rectangle overlay (to give clean rounded corners)
    # We draw a slightly transparent layer to smooth the gradient
    # Actually we'll use the rounded rect approach differently

    # === Book drop shadow ===
    shadow = Image.new('RGBA', (size, size), (0, 0, 0, 0))
    sdraw = ImageDraw.Draw(shadow)
    sdraw.ellipse([cx - s(148), cy + s(108), cx + s(148), cy + s(128)], fill=(0, 0, 0, 50))
    shadow = shadow.filter(ImageFilter.GaussianBlur(radius=max(1, s(8))))
    img = Image.alpha_composite(img, shadow)

    # === Book ===
    # Bottom page stack - left
    for dy in range(2):
        o = dy * 2
        draw.polygon([
            (cx - s(8) - o, cy - s(80) - o),
            (cx - s(130), cy + s(40) - o),
            (cx - s(130), cy + s(90) - o),
            (cx - s(8), cy + s(10) - o)
        ], fill=(PAGE_BOTTOM[0] - dy * 10, PAGE_BOTTOM[1] - dy * 8, PAGE_BOTTOM[2] - dy * 8, 200 - dy * 40))

    # Left page
    draw.polygon([
        (cx - s(4), cy - s(76)),
        (cx - s(126), cy + s(44)),
        (cx - s(126), cy + s(86)),
        (cx - s(4), cy + s(14))
    ], fill=PAGE_L)

    # Bottom page stack - right
    for dy in range(2):
        o = dy * 2
        draw.polygon([
            (cx + s(8) + o, cy - s(80) - o),
            (cx + s(130), cy + s(40) - o),
            (cx + s(130), cy + s(90) - o),
            (cx + s(8), cy + s(10) - o)
        ], fill=(PAGE_BOTTOM[0] - dy * 10, PAGE_BOTTOM[1] - dy * 8, PAGE_BOTTOM[2] - dy * 8, 200 - dy * 40))

    # Right page
    draw.polygon([
        (cx + s(4), cy - s(76)),
        (cx + s(126), cy + s(44)),
        (cx + s(126), cy + s(86)),
        (cx + s(4), cy + s(14))
    ], fill=PAGE_R)

    # Spine shadow
    draw.rectangle([cx - s(4), cy - s(76), cx + s(4), cy + s(14)], fill=(0, 0, 0, 16))

    # Page lines - left
    for i in range(4):
        y_off = cy - s(56) + i * s(20)
        x_left = cx - s(110) + i * s(5)
        draw.line([
            (cx - s(8), y_off),
            (x_left, y_off + s(56))
        ], fill=(0, 0, 0, 12), width=max(1, s(2)))

    # Page lines - right
    for i in range(4):
        y_off = cy - s(56) + i * s(20)
        x_right = cx + s(110) - i * s(5)
        draw.line([
            (cx + s(8), y_off),
            (x_right, y_off + s(56))
        ], fill=(0, 0, 0, 12), width=max(1, s(2)))

    # === Bookmark ribbon ===
    bm = [(cx - s(4), cy - s(76)),
          (cx - s(4), cy - s(110)),
          (cx + s(12), cy - s(98)),
          (cx + s(28), cy - s(110)),
          (cx + s(28), cy - s(84)),
          (cx + s(12), cy - s(72))]
    draw.polygon(bm, fill=GOLD)
    draw.line([bm[0], bm[1]], fill=GOLD_DARK, width=max(1, s(1)))
    draw.line([bm[1], bm[2]], fill=GOLD_DARK, width=max(1, s(1)))
    draw.line([bm[3], bm[4]], fill=GOLD_DARK, width=max(1, s(1)))
    draw.line([bm[4], bm[5]], fill=GOLD_DARK, width=max(1, s(1)))
    draw.line([bm[5], bm[0]], fill=GOLD_DARK, width=max(1, s(1)))

    # === Decorative text lines on pages ===
    for x_base, y_base in [(cx - s(70), cy + s(36)), (cx + s(70), cy + s(36))]:
        for row in range(3):
            y = y_base + row * s(14)
            for col in range(5):
                x = x_base + (col - 2) * s(18)
                draw.rectangle([x, y, x + s(12), y + max(1, s(1))],
                               fill=TEXT_CLR + (80,))

    # === Corner radius mask ===
    # Create a mask with rounded corners
    mask = Image.new('L', (size, size), 0)
    mdraw = ImageDraw.Draw(mask)
    mdraw.rounded_rectangle([s(16), s(16), s(496), s(496)],
                            radius=s(96), fill=255)
    # Apply mask
    img = Image.alpha_composite(
        Image.new('RGBA', (size, size), (0, 0, 0, 0)),
        Image.composite(img, Image.new('RGBA', (size, size), (0, 0, 0, 0)), mask)
    )

    # === Subtle highlight overlay ===
    highlight = Image.new('RGBA', (size, size), (0, 0, 0, 0))
    hdraw = ImageDraw.Draw(highlight)
    hdraw.rounded_rectangle([s(16), s(16), s(496), s(496)],
                            radius=s(96), fill=(255, 255, 255, 18))
    img = Image.alpha_composite(img, highlight)

    return img


def main():
    print("Generating AZW3 Reader icons...")

    # Create base icon
    img_512 = create_base_icon(512)

    # ICO sizes
    ico_sizes = [16, 32, 48, 64, 96, 128, 256]

    # Save ICO
    ico_path = os.path.join(OUTPUT_DIR, 'app-icon.ico')
    img_512.save(ico_path, format='ICO', sizes=[(s, s) for s in ico_sizes])
    print(f"[OK] ICO: {ico_path} ({os.path.getsize(ico_path)} bytes)")

    # Save high-res PNG
    png_path = os.path.join(OUTPUT_DIR, 'app-icon.png')
    img_512.save(png_path, format='PNG')
    print(f"[OK] PNG: {png_path}")

    # Individual PNGs
    png_dir = os.path.join(OUTPUT_DIR, 'png')
    os.makedirs(png_dir, exist_ok=True)
    for s in ico_sizes:
        resized = img_512.resize((s, s), Image.LANCZOS)
        resized.save(os.path.join(png_dir, f'icon_{s}x{s}.png'), format='PNG')
    print(f"[OK] PNGs: {png_dir}")

    print("[OK] Done!")


if __name__ == '__main__':
    main()
