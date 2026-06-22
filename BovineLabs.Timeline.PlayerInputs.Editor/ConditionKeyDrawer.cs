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

        private class AssetPostprocessor : UnityEditor.AssetPostprocessor
        {
            private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets,
                string[] movedAssets, string[] movedFromAssetPaths)
            {
                foreach (var path in importedAssets)
                    if (path.EndsWith(".asset"))
                    {
                        s_Cache = null;
                        return;
                    }

                if (deletedAssets.Length > 0 || movedAssets.Length > 0) s_Cache = null;
            }
        }
    }
}
#endif