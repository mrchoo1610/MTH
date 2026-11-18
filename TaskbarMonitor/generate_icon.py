import math
from PIL import Image, ImageDraw, ImageFilter

def create_icon(output_ico_path, output_png_path):
    size = 512
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)

    # 1. Subtle Outer Glow
    glow_box = [28, 28, size - 28, size - 28]
    corner_radius = 110

    # Draw glow layer
    glow_img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    glow_draw = ImageDraw.Draw(glow_img)
    glow_draw.rounded_rectangle(glow_box, radius=corner_radius, fill=(16, 185, 129, 60))
    glow_img = glow_img.filter(ImageFilter.GaussianBlur(18))
    img.paste(glow_img, (0, 0), glow_img)

    # 2. Main Chip Body (Rounded Square)
    body_box = [36, 36, size - 36, size - 36]
    # Dark metallic base
    draw.rounded_rectangle(body_box, radius=corner_radius, fill=(15, 23, 42, 255), outline=(56, 189, 248, 200), width=6)

    # Inner subtle rim
    inner_rim = [46, 46, size - 46, size - 46]
    draw.rounded_rectangle(inner_rim, radius=corner_radius - 8, outline=(51, 65, 85, 160), width=3)

    # 3. Chip Connector Pins (Left, Right, Top, Bottom)
    pin_color = (148, 163, 184, 230)
    pin_g = (52, 211, 153, 220) # Green accent pin
    pin_len = 22
    
    # 4 pins on each edge
    pin_positions = [150, 220, 292, 362]
    
    for i, p in enumerate(pin_positions):
        c = pin_g if i in (1, 2) else pin_color
        # Top pins
        draw.rounded_rectangle([p - 10, 16, p + 10, 36], radius=4, fill=c)
        # Bottom pins
        draw.rounded_rectangle([p - 10, size - 36, p + 10, size - 16], radius=4, fill=c)
        # Left pins
        draw.rounded_rectangle([16, p - 10, 36, p + 10], radius=4, fill=c)
        # Right pins
        draw.rounded_rectangle([size - 36, p - 10, size - 16, p + 10], radius=4, fill=c)

    # 4. Silicon Core (Inner Die)
    die_box = [115, 115, size - 115, size - 115]
    die_radius = 48
    draw.rounded_rectangle(die_box, radius=die_radius, fill=(11, 15, 26, 255), outline=(30, 41, 59, 240), width=4)

    # 5. Circuit Traces to Corners
    trace_color = (51, 65, 85, 200)
    trace_accent = (56, 189, 248, 180)
    # Top-Left trace
    draw.line([(115, 160), (70, 160)], fill=trace_color, width=4)
    draw.line([(160, 115), (160, 70)], fill=trace_color, width=4)
    draw.ellipse([64, 154, 76, 166], fill=trace_accent)
    draw.ellipse([154, 64, 166, 76], fill=trace_accent)

    # Top-Right trace
    draw.line([(size - 115, 160), (size - 70, 160)], fill=trace_color, width=4)
    draw.line([(size - 160, 115), (size - 160, 70)], fill=trace_color, width=4)
    draw.ellipse([size - 76, 154, size - 64, 166], fill=trace_accent)
    draw.ellipse([size - 166, 64, size - 154, 76], fill=trace_accent)

    # Bottom-Left trace
    draw.line([(115, size - 160), (70, size - 160)], fill=trace_color, width=4)
    draw.line([(160, size - 115), (160, size - 70)], fill=trace_color, width=4)
    draw.ellipse([64, size - 166, 76, size - 154], fill=trace_accent)
    draw.ellipse([154, size - 76, 166, size - 64], fill=trace_accent)

    # Bottom-Right trace
    draw.line([(size - 115, size - 160), (size - 70, size - 160)], fill=trace_color, width=4)
    draw.line([(size - 160, size - 115), (size - 160, size - 70)], fill=trace_color, width=4)
    draw.ellipse([size - 76, size - 166, size - 64, size - 154], fill=trace_accent)
    draw.ellipse([size - 166, size - 76, size - 154, size - 64], fill=trace_accent)

    # 6. Real-Time Telemetry Pulse / Performance Curve across the Die
    # A glowing ECG / Speed pulse line (Cyan & Emerald)
    pulse_points = [
        (135, 256),
        (175, 256),
        (205, 220),
        (230, 310),
        (256, 160),
        (282, 330),
        (308, 235),
        (335, 256),
        (377, 256)
    ]

    # Pulse Glow Layer
    pulse_glow = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    pg_draw = ImageDraw.Draw(pulse_glow)
    for i in range(len(pulse_points) - 1):
        pg_draw.line([pulse_points[i], pulse_points[i+1]], fill=(52, 211, 153, 140), width=18)
    pulse_glow = pulse_glow.filter(ImageFilter.GaussianBlur(10))
    img.paste(pulse_glow, (0, 0), pulse_glow)

    # Sharp Pulse Line
    for i in range(len(pulse_points) - 1):
        # Gradient effect from Cyan to Emerald
        ratio = i / (len(pulse_points) - 1)
        r = int(56 * (1 - ratio) + 52 * ratio)
        g = int(189 * (1 - ratio) + 211 * ratio)
        b = int(248 * (1 - ratio) + 153 * ratio)
        draw.line([pulse_points[i], pulse_points[i+1]], fill=(r, g, b, 255), width=8)

    # Peak Indicator Dot (The top performance spike)
    draw.ellipse([248, 152, 264, 168], fill=(248, 250, 252, 255), outline=(56, 189, 248, 255), width=3)
    draw.ellipse([274, 322, 290, 338], fill=(52, 211, 153, 255), outline=(16, 185, 129, 255), width=3)

    # Save PNG
    img.save(output_png_path, "PNG")

    # Generate multi-resolution ICO (16, 24, 32, 48, 64, 128, 256)
    ico_sizes = [(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)]
    img.save(output_ico_path, format="ICO", sizes=ico_sizes)
    print(f"Icon successfully generated at:\n  {output_ico_path}\n  {output_png_path}")

if __name__ == "__main__":
    import os
    target_dir = os.path.dirname(os.path.abspath(__file__))
    create_icon(
        os.path.join(target_dir, "app.ico"),
        os.path.join(target_dir, "app.png")
    )
