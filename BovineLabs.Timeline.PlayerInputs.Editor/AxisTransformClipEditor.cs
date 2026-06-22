using BovineLabs.Timeline.PlayerInputs.Authoring;
using BovineLabs.Timeline.PlayerInputs.Data;
using UnityEditor;
using UnityEngine;

namespace BovineLabs.Timeline.PlayerInputs.Editor
{
    // Mode-aware inspector: a clip is EITHER Move or Aim, so only the relevant group is shown. Move hides the Aim
    // turn-speed; Aim hides Range/Leash/SnapBackOnRelease (those do nothing in Aim - Aim always holds the last
    // direction). Stops the "Snap Back On Release does nothing on my Aim clip" confusion.
    [CustomEditor(typeof(AxisTransformClip))]
    public class AxisTransformClipEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var modeProp = serializedObject.FindProperty("Mode");
            var isAim = modeProp != null && modeProp.enumValueIndex == (int)AxisTransformMode.Aim;

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
                if (!isAim && n == "Smoothing")
                    continue;
                EditorGUILayout.PropertyField(it, true);
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
