using System.IO;
using BovineLabs.Timeline.PlayerInputs.Authoring;
using BovineLabs.Timeline.PlayerInputs.Data;
using UnityEditor;
using UnityEngine;

namespace BovineLabs.Timeline.PlayerInputs.Editor
{
    // Mode-aware inspector: a clip is EITHER Move or Aim, so only the relevant group is shown. Move hides the Aim
    // turn-speed; Aim hides Range/Leash/SnapBackOnRelease (those do nothing in Aim - Aim always holds the last
    // direction). Stops the "Snap Back On Release does nothing on my Aim clip" confusion.
    //
    // Hovering the CameraRelative toggle pops a generated SVG icon (rasterised via the Vector Graphics module)
    // explaining what the option does. Right-click the header → "Export concept SVGs" to save them as assets.
    [CustomEditor(typeof(AxisTransformClip))]
    public class AxisTransformClipEditor : UnityEditor.Editor
    {
        private Rect cameraRelativeRow;
        private bool cameraRelativeValue;
        private bool hasCameraRelativeRow;
        private GUIStyle captionStyle;

        // Continuously repaint while selected so the hover overlay tracks the mouse responsively.
        public override bool RequiresConstantRepaint()
        {
            return true;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var modeProp = serializedObject.FindProperty("Mode");
            var isAim = modeProp != null && modeProp.enumValueIndex == (int)AxisTransformMode.Aim;

            var aimAtCursorProp = serializedObject.FindProperty("AimAtCursor");
            var aimAtCursor = isAim && aimAtCursorProp != null && aimAtCursorProp.boolValue;

            this.hasCameraRelativeRow = false;

            var it = serializedObject.GetIterator();
            var enterChildren = true;
            while (it.NextVisible(enterChildren))
            {
                enterChildren = false;
                var n = it.name;
                if (n == "m_Script")
                    continue;
                if (isAim && (n == "Range" || n == "LeashRadius" || n == "SnapBackOnRelease"))
                    continue;
                if (!isAim && (n == "Smoothing" || n == "AimRadius" || n == "RotateInPlace" || n == "AimAtCursor"))
                    continue;
                // Cursor aim uses the global pointer, not the stick Action or the camera-relative basis - hide both
                // so the inspector doesn't show (and the hover overlay doesn't explain) options that do nothing here.
                if (aimAtCursor && (n == "Action" || n == "CameraRelative"))
                    continue;

                EditorGUILayout.PropertyField(it, true);

                if (n == "CameraRelative")
                {
                    this.cameraRelativeRow = GUILayoutUtility.GetLastRect();
                    this.cameraRelativeValue = it.boolValue;
                    this.hasCameraRelativeRow = true;
                }
            }

            serializedObject.ApplyModifiedProperties();

            // Draw the hover icon as a floating, non-layout overlay so the GUILayout stream stays identical between
            // the Layout and Repaint passes (a conditional GUILayout box here would throw a state-mismatch error).
            if (this.hasCameraRelativeRow && Event.current.type == EventType.Repaint &&
                this.cameraRelativeRow.Contains(Event.current.mousePosition))
            {
                this.DrawConceptOverlay(this.cameraRelativeRow, this.cameraRelativeValue);
            }
        }

        private void DrawConceptOverlay(Rect row, bool on)
        {
            this.captionStyle ??= new GUIStyle(EditorStyles.wordWrappedMiniLabel) { richText = false };

            var tex = SvgIconRaster.Get(
                on ? "axis.camrel.on" : "axis.camrel.off", AxisConceptSvg.CameraRelative(on), 256);

            var caption = on
                ? "Camera-relative: the stick is read relative to the Main Camera. Its forward/right are projected "
                  + "onto the Plane, so pushing up always moves AWAY from the camera — wherever it faces."
                : "World axes: the stick maps to fixed world axes on the Plane (Up=(0,1,0) → X/Z), ignoring where "
                  + "the camera looks. Pushing up is +Z every time.";

            const float pad = 6f;
            const float icon = 116f;
            var height = icon + (pad * 2f);
            var overlay = new Rect(row.x, row.yMax + 2f, Mathf.Max(row.width, 300f), height);

            // Shadow + panel.
            GUI.color = new Color(0f, 0f, 0f, 0.25f);
            GUI.DrawTexture(new Rect(overlay.x + 2f, overlay.y + 2f, overlay.width, overlay.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Box(overlay, GUIContent.none, EditorStyles.helpBox);

            var iconRect = new Rect(overlay.x + pad, overlay.y + pad, icon, icon);
            if (tex != null)
            {
                GUI.DrawTexture(iconRect, tex, ScaleMode.ScaleToFit, true);
            }

            var textRect = new Rect(iconRect.xMax + pad, overlay.y + pad, overlay.width - icon - (pad * 3f), icon);
            GUI.Label(textRect, caption, this.captionStyle);
        }

        [MenuItem("CONTEXT/AxisTransformClip/Export concept SVGs")]
        private static void ExportConceptSvgs(MenuCommand command)
        {
            var folder = EditorUtility.SaveFolderPanel("Export AxisTransform concept SVGs", "Assets", string.Empty);
            if (string.IsNullOrEmpty(folder))
            {
                return;
            }

            File.WriteAllText(Path.Combine(folder, "AxisTransform_CameraRelative_On.svg"), AxisConceptSvg.CameraRelative(true));
            File.WriteAllText(Path.Combine(folder, "AxisTransform_CameraRelative_Off.svg"), AxisConceptSvg.CameraRelative(false));

            AssetDatabase.Refresh();
            Debug.Log(
                $"[AxisTransform] Exported concept SVGs to {folder}. If inside Assets/, set each importer's " +
                "'Generated Asset Type' to 'UI Toolkit Vector Image' to use them as UITK VectorImages.");
        }
    }
}
