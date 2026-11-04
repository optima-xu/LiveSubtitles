# LiveSubtitles icon

Red circular mark with white **LS** lettering, based on the user's supplied KX reference. `livesubtitles.png` is the original transparent generated asset. `livesubtitles.ico` contains 16, 24, 32, 48, 64, 128 and 256 pixel variants for Windows. `livesubtitles-preview.png` is a 256 pixel preview. Resizing and ICO encoding preserve the generated artwork and alpha channel.

The ICO is embedded in the executable, WPF window resources and notification-area icon. No external runtime asset path is required.

Generated using the built-in `image_gen` tool; the CLI fallback was not used. Final edit prompt:

> Use case: precise-object-edit. Image 1 is the edit target, the LS application icon. Image 2 is the original reference for the flat red color and plain lightweight sans-serif typography. Refine image 1 into a literal clean flat vector-style app icon. Keep exactly the letters "LS", centered in a perfect red circular disk. IMPORTANT targeted corrections: the red disk must have an absolutely uniform flat fill RGB(240,73,58), no tonal variation, no gradient, no grain, no noise, no lighting. The red disk is fully opaque, alpha 255. The LS text is pure solid white, matching the regular/light font weight and relative size in image 2. The outer edge must be a smooth mathematically perfect circle with no stray colored pixels, speckles, protrusions, or fringe outside it. The background outside the circle is completely transparent, not a checkerboard drawing. Square canvas, even margin about 8% on every side, centered composition. No shadows, no outlines, no 3D, no extra text, no presentation board. Deliver one clean production icon.
