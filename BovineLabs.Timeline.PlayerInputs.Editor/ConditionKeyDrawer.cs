using System;
using System.Collections.Generic;
using BovineLabs.Reaction.Authoring.Conditions;
using BovineLabs.Reaction.Data.Conditions;
using BovineLabs.Timeline.Core;
using UnityEditor;
using UnityEngine;

#if UNITY_EDITOR
namespace BovineLabs.Timeline.PlayerInputs.Editor
{
    [CustomPropertyDrawer(typeof(ConditionKey))]
    public class ConditionKeyDrawer : PropertyDrawer
    {
        private static Dictionary<int, ConditionEventObject> s_Cache;

        private static HashSet<int> s_Missing;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var valueProp = FirstChild(property);

            EditorGUI.BeginProperty(position, label, property);

            var current = FindByKey(valueProp?.intValue ?? 0);
            var next = (ConditionEventObject)EditorGUI.ObjectField(
                position, label, current, typeof(ConditionEventObject), false);

            if (!ReferenceEquals(next, current) && valueProp != null) valueProp.intValue = next != null ? next.Key : 0;

            EditorGUI.EndProperty();
        }

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

            if (s_Missing.Contains(key)) return null;

            BuildCache();
            if (s_Cache.TryGetValue(key, out var obj2)) return obj2;

            s_Missing.Add(key);
            return null;
        }

        private static void BuildCache()
        {
            s_Cache = new Dictionary<int, ConditionEventObject>();
            s_Missing = new HashSet<int>();
            foreach (var guid in AssetDatabase.FindAssets("t:ConditionEventObject"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var obj = AssetDatabase.LoadAssetAtPath<ConditionEventObject>(path);
                if (obj != null)
                    s_Cache[obj.Key] = obj;
            }
        }

        // Only touch the cache when an imported/moved path actually resolves to a ConditionEventObject, and only the
        // touched entries - instead of nuking the whole cache on any .asset import, which forced a full
        // AssetDatabase.FindAssets rescan on the next repaint (an import-storm stall in big projects).
        private static void RefreshOne(string path, Type conditionType)
        {
            if (s_Cache == null || !path.EndsWith(".asset")) return;

            var type = AssetDatabase.GetMainAssetTypeAtPath(path);
            if (type == null || !conditionType.IsAssignableFrom(type)) return;

            var obj = AssetDatabase.LoadAssetAtPath<ConditionEventObject>(path);
            if (obj == null) return;

            // The object's Key may have just changed - drop any stale key it was previously cached under.
            RemoveByValue(obj);

            var key = obj.Key;
            if (key == 0) return;

            s_Cache[key] = obj;
            s_Missing?.Remove(key);
        }

        private static void RemoveByValue(ConditionEventObject obj)
        {
            List<int> stale = null;
            foreach (var kvp in s_Cache)
                if (ReferenceEquals(kvp.Value, obj))
                    (stale ??= new List<int>()).Add(kvp.Key);

            if (stale == null) return;
            foreach (var key in stale)
                s_Cache.Remove(key);
        }

        private class AssetPostprocessor : UnityEditor.AssetPostprocessor
        {
            private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets,
                string[] movedAssets, string[] movedFromAssetPaths)
            {
                // Nothing built yet -> the lazy BuildCache on next lookup already picks up everything; do no work.
                if (s_Cache == null) return;

                var conditionType = typeof(ConditionEventObject);

                foreach (var path in importedAssets)
                    RefreshOne(path, conditionType);

                foreach (var path in movedAssets)
                    RefreshOne(path, conditionType);

                // Deletions: we cannot type-check a path whose asset is gone, so drop any cache entry whose object was
                // destroyed by the delete (Unity's == null). Cheap - the cache holds only ConditionEventObjects.
                if (deletedAssets.Length > 0)
                {
                    List<int> destroyed = null;
                    foreach (var kvp in s_Cache)
                        if (kvp.Value == null)
                            (destroyed ??= new List<int>()).Add(kvp.Key);

                    if (destroyed != null)
                        foreach (var key in destroyed)
                            s_Cache.Remove(key);
                }
            }
        }
    }
}
#endif