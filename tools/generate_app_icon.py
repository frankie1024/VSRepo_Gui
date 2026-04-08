from __future__ import annotations

from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter


ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "Assets"
SVG_PATH = ASSETS / "AppIcon.svg"
PNG_PATH = ASSETS / "AppIcon.png"
ICO_PATH = ASSETS / "AppIcon.ico"

SIZE = 512
CANVAS_BOUNDS = (20, 20, 492, 492)


PALETTE = {
    "background": "#F4ECE3",
    "shadow": (68, 52, 42, 36),
    "fur": "#B97A52",
    "fur_dark": "#8A583A",
    "fur_mid": "#9B6647",
    "fur_light": "#E9D6C3",
    "line": "#6F4633",
    "leaf_dark": "#5F6742",
    "leaf_mid": "#798154",
    "pot": "#8B8E96",
    "pot_dark": "#6D717A",
    "accent": "#D59B39",
}


def svg_markup() -> str:
    return f"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512" fill="none">
  <rect x="20" y="20" width="472" height="472" rx="112" fill="{PALETTE["background"]}"/>
  <ellipse cx="255" cy="412" rx="144" ry="24" fill="#D9CEC2" opacity="0.52"/>

  <path d="M151 345C144 292 151 245 176 208C196 179 231 165 266 171C308 179 339 214 347 265C355 321 343 373 311 403C282 430 240 434 201 417C172 405 156 381 151 345Z" fill="{PALETTE["fur"]}"/>
  <path d="M171 309C168 272 174 242 191 219C207 197 232 187 257 190C285 194 308 215 316 247C325 285 320 330 301 365C286 393 262 411 236 410C209 409 182 389 174 355C170 340 171 326 171 309Z" fill="{PALETTE["fur_light"]}"/>
  <path d="M166 289C160 252 166 220 186 193C208 163 242 149 277 156C301 160 321 173 337 192C323 175 305 161 284 154C245 140 205 152 178 181C155 206 145 242 147 281C147 317 154 349 172 377C156 355 149 324 166 289Z" fill="#C88C63" opacity="0.55"/>

  <path d="M184 176C186 139 211 111 245 109C272 107 295 127 300 156C305 184 289 211 264 220C236 231 207 221 194 197C188 190 185 183 184 176Z" fill="{PALETTE["fur"]}"/>
  <path d="M201 137L220 96L241 138L201 137Z" fill="{PALETTE["fur"]}"/>
  <path d="M246 140L270 98L286 144L246 140Z" fill="{PALETTE["fur"]}"/>
  <path d="M209 135L221 108L236 136L209 135Z" fill="#E5C0A4"/>
  <path d="M251 138L267 110L279 141L251 138Z" fill="#E5C0A4"/>

  <path d="M187 207C193 198 201 192 212 189" stroke="{PALETTE["line"]}" stroke-width="8" stroke-linecap="round"/>
  <path d="M252 191C262 193 270 199 275 208" stroke="{PALETTE["line"]}" stroke-width="8" stroke-linecap="round"/>
  <circle cx="223" cy="178" r="6" fill="{PALETTE["line"]}"/>
  <path d="M244 189L257 193L246 200" stroke="{PALETTE["line"]}" stroke-width="5" stroke-linecap="round" stroke-linejoin="round"/>
  <path d="M248 196C239 203 231 207 222 210" stroke="{PALETTE["line"]}" stroke-width="4" stroke-linecap="round"/>
  <path d="M246 198C253 206 257 211 259 217" stroke="{PALETTE["line"]}" stroke-width="4" stroke-linecap="round"/>

  <path d="M216 231C195 238 180 255 171 281" stroke="{PALETTE["fur_dark"]}" stroke-width="17" stroke-linecap="round"/>
  <path d="M226 270C204 278 191 298 186 320" stroke="{PALETTE["fur_dark"]}" stroke-width="16" stroke-linecap="round"/>
  <path d="M236 317C217 325 205 342 201 363" stroke="{PALETTE["fur_dark"]}" stroke-width="15" stroke-linecap="round"/>

  <path d="M162 360C137 347 121 324 119 294C117 264 131 235 157 213" stroke="{PALETTE["fur_dark"]}" stroke-width="22" stroke-linecap="round"/>
  <path d="M154 361C145 376 144 394 152 409C159 421 171 430 186 432" stroke="{PALETTE["fur_dark"]}" stroke-width="20" stroke-linecap="round"/>

  <ellipse cx="227" cy="427" rx="19" ry="10" fill="{PALETTE["fur_light"]}"/>
  <ellipse cx="278" cy="427" rx="21" ry="10" fill="{PALETTE["fur_light"]}"/>

  <path d="M349 346H403L396 406H356L349 346Z" fill="{PALETTE["pot"]}"/>
  <path d="M349 346H403L397 360H355L349 346Z" fill="{PALETTE["pot_dark"]}"/>
  <path d="M374 334L387 334L385 346L376 346L374 334Z" fill="{PALETTE["accent"]}"/>
  <path d="M384 334C387 303 387 273 384 244" stroke="{PALETTE["leaf_dark"]}" stroke-width="6" stroke-linecap="round"/>
  <path d="M382 304C372 280 361 261 346 245" stroke="{PALETTE["leaf_mid"]}" stroke-width="6" stroke-linecap="round"/>
  <path d="M386 284C397 260 409 243 424 227" stroke="{PALETTE["leaf_dark"]}" stroke-width="6" stroke-linecap="round"/>
  <path d="M390 305C400 287 412 274 427 264" stroke="{PALETTE["leaf_mid"]}" stroke-width="5" stroke-linecap="round"/>
</svg>
"""


def rounded_background(image: Image.Image) -> None:
    mask = Image.new("L", image.size, 0)
    ImageDraw.Draw(mask).rounded_rectangle(CANVAS_BOUNDS, radius=112, fill=255)
    image.putalpha(mask)


def draw_background(image: Image.Image) -> None:
    draw = ImageDraw.Draw(image)
    draw.rounded_rectangle(CANVAS_BOUNDS, radius=112, fill=PALETTE["background"])
    draw.ellipse((111, 388, 399, 436), fill=PALETTE["shadow"])


def draw_cat(image: Image.Image) -> None:
    draw = ImageDraw.Draw(image)
    fur = PALETTE["fur"]
    fur_light = PALETTE["fur_light"]
    fur_dark = PALETTE["fur_dark"]
    line = PALETTE["line"]

    draw.polygon(
        [(151, 345), (150, 284), (166, 228), (199, 186), (248, 168), (301, 178), (335, 211), (350, 264), (347, 322), (333, 375), (303, 409), (250, 427), (198, 418), (166, 387)],
        fill=fur,
    )
    draw.pieslice((168, 186, 319, 412), start=74, end=280, fill=fur_light)

    draw.ellipse((186, 110, 302, 225), fill=fur)
    draw.polygon([(206, 137), (221, 97), (241, 141)], fill=fur)
    draw.polygon([(247, 141), (271, 100), (287, 146)], fill=fur)
    draw.polygon([(214, 136), (224, 112), (236, 138)], fill="#E5C0A4")
    draw.polygon([(254, 140), (267, 113), (279, 143)], fill="#E5C0A4")

    draw.line([(218, 180), (224, 175), (232, 176)], fill=line, width=6, joint="curve")
    draw.line([(255, 176), (264, 177), (271, 183)], fill=line, width=6, joint="curve")
    draw.ellipse((216, 172, 228, 184), fill=line)
    draw.polygon([(244, 190), (257, 193), (247, 200)], fill="#8E6556")
    draw.line([(247, 198), (236, 205), (224, 210)], fill=line, width=4)
    draw.line([(247, 198), (255, 208), (258, 215)], fill=line, width=4)

    draw.line([(214, 234), (196, 243), (181, 261), (171, 284)], fill=fur_dark, width=18, joint="curve")
    draw.line([(224, 272), (206, 282), (194, 300), (188, 322)], fill=fur_dark, width=17, joint="curve")
    draw.line([(236, 320), (220, 327), (209, 344), (204, 364)], fill=fur_dark, width=16, joint="curve")

    tail_layer = Image.new("RGBA", image.size, (0, 0, 0, 0))
    tail_draw = ImageDraw.Draw(tail_layer)
    tail_draw.line([(165, 358), (139, 345), (124, 324), (120, 295), (124, 268), (138, 243), (159, 221)], fill=fur, width=30, joint="curve")
    tail_draw.line([(165, 360), (141, 350), (126, 328), (123, 297), (129, 270), (142, 247), (159, 228)], fill=fur_dark, width=12, joint="curve")
    tail_layer = tail_layer.filter(ImageFilter.GaussianBlur(0.3))
    image.alpha_composite(tail_layer)

    draw.ellipse((209, 417, 245, 437), fill=fur_light)
    draw.ellipse((258, 417, 298, 438), fill=fur_light)


def draw_pot(image: Image.Image) -> None:
    draw = ImageDraw.Draw(image)
    draw.polygon([(349, 346), (403, 346), (396, 406), (356, 406)], fill=PALETTE["pot"])
    draw.polygon([(349, 346), (403, 346), (397, 360), (355, 360)], fill=PALETTE["pot_dark"])
    draw.polygon([(374, 334), (387, 334), (385, 346), (376, 346)], fill=PALETTE["accent"])

    draw.line([(384, 334), (387, 298), (386, 262), (384, 244)], fill=PALETTE["leaf_dark"], width=6)
    draw.line([(382, 304), (372, 280), (359, 259), (346, 245)], fill=PALETTE["leaf_mid"], width=6)
    draw.line([(386, 284), (399, 259), (412, 241), (424, 227)], fill=PALETTE["leaf_dark"], width=6)
    draw.line([(390, 305), (401, 287), (414, 274), (427, 264)], fill=PALETTE["leaf_mid"], width=5)


def build_icon() -> Image.Image:
    image = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    draw_background(image)
    draw_cat(image)
    draw_pot(image)
    rounded_background(image)
    return image


def main() -> None:
    ASSETS.mkdir(exist_ok=True)
    SVG_PATH.write_text(svg_markup(), encoding="utf-8")
    icon = build_icon()
    icon.save(PNG_PATH)
    icon.save(ICO_PATH, sizes=[(256, 256), (128, 128), (64, 64), (48, 48), (32, 32), (24, 24), (16, 16)])
    print(f"Generated {SVG_PATH}")
    print(f"Generated {PNG_PATH}")
    print(f"Generated {ICO_PATH}")


if __name__ == "__main__":
    main()
