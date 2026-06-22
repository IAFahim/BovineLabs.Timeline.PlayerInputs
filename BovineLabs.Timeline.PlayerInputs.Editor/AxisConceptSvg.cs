#if UNITY_EDITOR
namespace BovineLabs.Timeline.PlayerInputs.Editor
{
    /// <summary>
    /// Procedural SVG illustrations for AxisTransform concepts, shown on hover and exportable as UITK VectorImages.
    /// Top-down diagram: white dot = the body, cyan = forward axis, red = right axis, warm glyph = the Main Camera.
    /// </summary>
    internal static class AxisConceptSvg
    {
        /// <summary>
        /// CameraRelative ON: the axis frame is coupled to the camera (parallel, dashed link) — push-up = away from cam.
        /// CameraRelative OFF: the axis frame is world-fixed; the camera looks elsewhere but the axes don't follow.
        /// </summary>
        internal static string CameraRelative(bool on)
        {
            // ON  → frame follows camera: both tilted by the same angle, linked by a dashed line.
            // OFF → frame is world up; camera looks up-right, decoupled.
            var frameRot = on ? 12 : 0;
            var camPos = on ? "60,100" : "30,99";
            var camRot = on ? 12 : -40;
            var caption = on ? "camera-relative" : "world axes";

            var coupling = on
                ? "  <line x1=\"60\" y1=\"94\" x2=\"60\" y2=\"60\" stroke=\"#ffd86b66\" stroke-width=\"1.5\" stroke-dasharray=\"3 3\"/>\n"
                : "  <text x=\"96\" y=\"22\" fill=\"#ff6b6b\" font-family=\"monospace\" font-size=\"8\">+X</text>\n" +
                  "  <text x=\"66\" y=\"16\" fill=\"#5fd0ff\" font-family=\"monospace\" font-size=\"8\">+Z</text>\n";

            return
                "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 120 120\" width=\"120\" height=\"120\">\n" +
                "  <rect x=\"0\" y=\"0\" width=\"120\" height=\"120\" rx=\"8\" fill=\"#15151a\"/>\n" +
                "  <ellipse cx=\"60\" cy=\"66\" rx=\"46\" ry=\"18\" fill=\"#ffffff08\" stroke=\"#ffffff14\" stroke-width=\"1\"/>\n" +
                coupling +
                $"  <g transform=\"translate(60,58) rotate({frameRot})\">\n" +
                "    <line x1=\"0\" y1=\"0\" x2=\"0\" y2=\"-26\" stroke=\"#5fd0ff\" stroke-width=\"2.5\" stroke-linecap=\"round\"/>\n" +
                "    <polygon points=\"0,-33 -4.5,-24 4.5,-24\" fill=\"#5fd0ff\"/>\n" +
                "    <line x1=\"0\" y1=\"0\" x2=\"24\" y2=\"0\" stroke=\"#ff6b6b\" stroke-width=\"2.5\" stroke-linecap=\"round\"/>\n" +
                "    <polygon points=\"31,0 22,-4.5 22,4.5\" fill=\"#ff6b6b\"/>\n" +
                "    <circle cx=\"0\" cy=\"0\" r=\"3\" fill=\"#ffffff\"/>\n" +
                "  </g>\n" +
                $"  <g transform=\"translate({camPos}) rotate({camRot})\">\n" +
                "    <polygon points=\"0,-23 -11,-5 11,-5\" fill=\"#ffd86b22\" stroke=\"#ffd86b66\" stroke-width=\"1\"/>\n" +
                "    <rect x=\"-8\" y=\"-5\" width=\"16\" height=\"11\" rx=\"2\" fill=\"#cfd2d6\"/>\n" +
                "    <rect x=\"-3\" y=\"-9\" width=\"6\" height=\"5\" rx=\"1\" fill=\"#cfd2d6\"/>\n" +
                "  </g>\n" +
                $"  <text x=\"60\" y=\"114\" fill=\"#9aa0a6\" font-family=\"monospace\" font-size=\"9\" text-anchor=\"middle\">{caption}</text>\n" +
                "</svg>\n";
        }
    }
}
#endif
