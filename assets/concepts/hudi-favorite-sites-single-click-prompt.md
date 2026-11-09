# HUDI favorite sites — single click and motion

This is a concept, not a change to the installed application.

Image: `hudi-favorite-sites-single-click.png`, generated with the built-in `image_gen` tool from `hudi-favorite-sites.png`.

Interactive motion preview: `hudi-favorite-sites-motion.html`. It uses the existing HUDI neutral sprite and renders the approved spherical icons as CSS sprites from the concept image; no image pixels were edited for the preview.

## Interaction

- One left click on HUDI toggles the sites.
- Each sphere moves upward from BEHIND the character, starts fully transparent, gradually fades in throughout its entire movement, and grows from 0.18 to 1. Full opacity is reached only as it finishes settling into place. HUDI always renders above the spheres, occluding them as they emerge and retract. The travel is curved with a spring overshoot.
- Opening takes 380 ms per sphere with a stagger of at most 30 ms. The total stagger is capped at 120 ms so larger collections still open within 500 ms. The arc is symmetric with equal angular spacing. The quicker launch covers 90% of the path early, overshoots by up to 10 pixels, scales briefly to 114%, and settles with a small spring rebound.
- Closing folds the spheres back toward the character over 230 ms, with a reverse stagger of at most 20 ms, capped at 80 ms total.
- The arc radius remains fixed at 238 logical pixels. Sphere diameter is the smaller of 64 pixels and the distance between neighboring sphere centers minus a 10 pixel gap: `min(64, 2 * 238 * sin(pi / (2 * (count - 1))) - 10)`. One site uses 64 pixels and sits at the apex. The preview controls support 1–20 sites; 5 sites use 64 pixel spheres, 20 sites use approximately 29 pixel spheres. The whole stage also scales to fit narrow windows.
- A density slider demonstrates this behavior; repeated example website icons are placeholders for additional saved websites.
- Escape or clicking the background closes the launcher. Reduced motion uses an immediate state change.
- Pressing a sphere compresses its visual surface to 86% with a restrained brightness increase. Releasing after a click plays a 300 ms spring sequence (86% → 109% → 98% → 100%) and highlights the selected sphere. This inner-surface animation is independent of the sphere's travel and adaptive diameter. Repeated presses restart cleanly. Reduced motion keeps only the color feedback.
- Website buttons in the preview animate and highlight selection; they do not navigate.
- The existing application gestures remain double click for notes, triple click for the model dialog, and dragging for moving HUDI. An application implementation must arbitrate clicks and dragging so those gestures do not accidentally activate a site.

## Final image generation prompt

Edit the supplied HUDI favorite sites UI concept with one precise text change only. In the square outlined label beneath the character, replace the text “Ctrl + двойной клик” with exactly “Один клик ЛКМ”. Preserve all other elements, all existing website spheres and their positions, the full-body grey hoodie robot identity, the matte dark desktop, the context menu, the settings form, all other Russian text, all proportions, colors, lighting and square panel corners. Keep the same landscape composition. This is the final resting layout of a dynamic spring animation in which the spheres rise from the character one after another, settle into this semicircle, and retract on another single left mouse click; do not add motion trails, arrows, captions, animation storyboards, or extra elements to the image.
