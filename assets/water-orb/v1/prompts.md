# Water orb v1

## Alpha extraction

Попытка редактирования встроенным `image_gen`; результат отклонён из-за
полупрозрачной заливки вне сфер. Финальный вырезанный лист получен хромакеем
из оригинального зелёного листа, а не из этого отклонённого результата.

Use case: background-extraction.
Edit the supplied sprite sheet. Remove ONLY the green chroma background and green transmitted through the water, producing a genuine RGBA transparent sprite sheet.
Keep the exact 4-by-4 grid of sixteen water-orb animation frames, exact canvas proportions, all frame centers and sizes, same shapes, same highlights and same cell ordering. Do not redesign or reposition anything. Do not add any text or frame borders.
These are clear liquid-water spheres: their interiors must be truly mostly transparent, not an opaque white or gray fill. Preserve the delicate cyan-blue watery outlines and flowing white caustics with smooth PARTIAL ALPHA so the future desktop background remains visible through them. Make the open central regions nearly transparent (approximately 0–15 percent opacity), watery edges partially transparent, strongest white glints more opaque. Remove green spill without deleting pale blue or white highlights. No green tint. Outside every orb must be alpha zero. No checkerboard, no solid replacement background, no floor or shadow.
Deliver the same sixteen-frame 4x4 sprite sheet as a real transparent PNG, preserving the original animation sequence.

Generated with the built-in image_gen tool. No Higgsfield generation credits used.

## Green-screen sprite sheet

Use case: stylized-concept.
Asset type: a production animation sprite sheet for a small watery desktop UI orb, on chroma green for later extraction.
Create ONE square 4-by-4 sprite sheet, exactly 16 equally sized cells, ordered left to right then top to bottom. No visible grid, no text or numbering. Every cell contains the SAME centered clear liquid-water sphere at the SAME scale, camera and center. Orb diameter approximately 68% of each cell width; keep generous identical green margins so no orb touches another cell.
Background: perfectly flat saturated chroma green #00FF00, absolutely uniform, including all gaps, no floor, no shadow, no gradient or scene.
The orb is alive and made of clear water, not a metal ball or opaque pearl. Thin translucent watery cyan-blue rim, soft white reflected highlights, delicate internal caustics flowing along the edge, mostly clear quiet center with the green backdrop visibly transmitted through it. Subtle pale blue reflections only, no heavy rainbow iridescence. Highly polished smooth antialiased edges, no fog or outer aura. Keep the center readable for an icon that will be placed later; do not draw any icon.
The 16 cells are evenly spaced consecutive phases of one seamless 4-second fluid wobble cycle: nearly round water volume gently becoming a small tilted oval, subtle asymmetric ripples travel around its outline, moving curved highlights and refraction give the feeling of flowing water. Deformation restrained to roughly 5–8 percent, no detached drops, no splashes, no popping, no changing total volume, no drifting center. The last phase must transition smoothly back into the first. Consistent identity and lighting throughout, no random redesign between cells.
No character, no scenery, no letters, no symbols, no watermark, no checkerboard.

## Alpha correction — rejected

Повторная попытка через встроенный `image_gen`; отклонена из-за артефактов.

Correct the alpha channel of this existing 4x4 water-orb sprite sheet. This is a technical background-extraction edit, not a redesign.
The previous result has an unwanted pale rectangular fog across the entire canvas. REMOVE that fog completely. All pixels OUTSIDE the sixteen individual water silhouettes, including ALL FOUR CORNERS and the entire gaps between cells, must have EXACTLY ZERO ALPHA (fully transparent). No semitransparent white backdrop, no blue backdrop, no checkerboard. Keep only the liquid rims and specular reflections of the sixteen orbs.
Keep the exact image dimensions, 4-by-4 grid, frame order, alignment, per-frame shapes, and lighting features. The inner clear opening of each orb should also be completely transparent, except where an actual narrow reflected light or caustic crosses it. White and cyan watery edges should retain partial alpha. No opaque sphere interiors. Only isolated water highlights and liquid contours floating on true transparency.
Output a clean reusable RGBA PNG sprite sheet with genuinely empty transparent space, not a photograph of a sheet or water bubbles on a light surface.
