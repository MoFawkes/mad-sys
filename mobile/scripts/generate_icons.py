"""Generate Expo icons from the desktop application's largest ICO frame."""

from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "assets" / "app.ico"
OUTPUT = ROOT / "mobile" / "assets" / "images"
NAVY = (17, 37, 73, 255)
CREAM = (244, 240, 230, 255)
CANVAS_SIZE = 1024
MARK_SIZE = 614


def load_mark() -> Image.Image:
    with Image.open(SOURCE) as icon:
        frame = icon.ico.getimage((256, 256)).convert("RGBA")
    # The ICO stores its pale canvas as opaque pixels, so derive the silhouette
    # from luminance instead of trusting the frame's alpha channel.
    alpha = frame.convert("L").point(lambda value: 255 if value < 180 else 0)
    frame.putalpha(alpha)
    bounds = alpha.getbbox()
    if bounds is None:
        raise RuntimeError("The desktop icon has no visible pixels.")
    cropped = frame.crop(bounds)
    ratio = min(MARK_SIZE / cropped.width, MARK_SIZE / cropped.height)
    resized = cropped.resize(
        (round(cropped.width * ratio), round(cropped.height * ratio)),
        Image.Resampling.LANCZOS,
    )
    # The source is a flat silhouette. Restore hard edges after enlargement.
    thresholded_alpha = resized.getchannel("A").point(lambda value: 255 if value >= 128 else 0)
    mark = Image.new("RGBA", resized.size, NAVY)
    mark.putalpha(thresholded_alpha)
    return mark


def centered(mark: Image.Image, background: tuple[int, int, int, int]) -> Image.Image:
    canvas = Image.new("RGBA", (CANVAS_SIZE, CANVAS_SIZE), background)
    position = (
        (CANVAS_SIZE - mark.width) // 2,
        (CANVAS_SIZE - mark.height) // 2,
    )
    canvas.alpha_composite(mark, position)
    return canvas


def recolor(mark: Image.Image, color: tuple[int, int, int, int]) -> Image.Image:
    result = Image.new("RGBA", mark.size, color)
    result.putalpha(mark.getchannel("A"))
    return result


def main() -> None:
    OUTPUT.mkdir(parents=True, exist_ok=True)
    mark = load_mark()

    icon = centered(mark, CREAM)
    icon.convert("RGB").save(OUTPUT / "icon.png", optimize=True)

    adaptive = centered(mark, (0, 0, 0, 0))
    adaptive.save(OUTPUT / "adaptive-icon.png", optimize=True)

    splash = centered(recolor(mark, CREAM), (0, 0, 0, 0))
    splash.save(OUTPUT / "splash-icon.png", optimize=True)

    favicon = icon.resize((196, 196), Image.Resampling.LANCZOS)
    favicon.convert("RGB").save(OUTPUT / "favicon.png", optimize=True)


if __name__ == "__main__":
    main()
