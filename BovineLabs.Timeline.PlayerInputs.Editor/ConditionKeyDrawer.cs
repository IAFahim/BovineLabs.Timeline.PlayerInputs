// <copyright file="ConditionKeyDrawer.cs" company="BovineLabs">
//     Copyright (c) BovineLabs. All rights reserved.
// </copyright>

using System.Collections.Generic;
using BovineLabs.Reaction.Authoring.Conditions;
using BovineLabs.Reaction.Data.Conditions;
using UnityEditor;
using UnityEngine;

#if UNITY_EDITOR
namespace BovineLabs.Timeline.PlayerInputs.Editor
{
    [CustomPropertyDrawer(typeof(ConditionKey))]
    public class ConditionKeyDrawer : PropertyDrawer
    {
        // key (ushort/int) → asset — rebuilt lazily, cleared on any change
        private static Dictionary<int, ConditionEventObject> s_Cache;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            // ConditionKey's first serialised child holds the raw integer value
            var valueProp = FirstChild(property);

            EditorGUI.BeginProperty(position, label, property);

            var current = FindByKey(valueProp?.intValue ?? 0);
            var next = (ConditionEventObject)EditorGUI.ObjectField(
                position, label, current, typeof(ConditionEventObject), false);

            if (!ReferenceEquals(next, current) && valueProp != null)
            {
                valueProp.intValue = next != null ? next.Key : 0;
            }

            EditorGUI.EndProperty();
        }

        // ── helpers ──────────────────────────────────────────────────────────

        private static SerializedProperty FirstChild(SerializedProperty prop)
        {
            var copy = prop.Copy();
            return copy.Next(true) ? copy : null;
        }

        private static ConditionEventObject FindByKey(int key)
        {
            if (key == 0) return null;
            if (s_Cache == null) BuildCache();
            if (s_Cache.TryGetValue(key, out var obj)) return obj;

            // Rebuild if not found in case of new assets
            BuildCache();
            return s_Cache.TryGetValue(key, out var obj2) ? obj2 : null;
        }

        private static void BuildCache()
        {
            s_Cache = new Dictionary<int, ConditionEventObject>();
            foreach (var guid in AssetDatabase.FindAssets("t:ConditionEventObject"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var obj = AssetDatabase.LoadAssetAtPath<ConditionEventObject>(path);
                if (obj != null)
                    s_Cache[obj.Key] = obj;
            }
        }

        private class AssetPostprocessor : UnityEditor.AssetPostprocessor
        {
            private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets,
                string[] movedAssets, string[] movedFromAssetPaths)
            {
                foreach (var path in importedAssets)
                {
                    if (path.EndsWith(".asset"))
                    {
                        s_Cache = null;
                        return;
                    }
                }

                if (deletedAssets.Length > 0 || movedAssets.Length > 0)
                {
                    s_Cache = null;
                }
            }
        }
    }
}
#endif