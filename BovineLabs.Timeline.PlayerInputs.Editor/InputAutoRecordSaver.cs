using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BovineLabs.Timeline.PlayerInputs.Data;
using Unity.Collections;
using Unity.Entities;
using UnityEditor;
using UnityEngine;

namespace BovineLabs.Timeline.PlayerInputs.Editor
{
    /// <summary>
    /// Writes every auto-armed recording to a ScriptableObject when play mode ends, and prunes the oldest so the
    /// folder cannot grow without bound.
    /// </summary>
    /// <remarks>
    /// WHY IT SAVES ON EXITING, NOT ON EXITED
    ///
    /// <see cref="PlayModeStateChange.ExitingPlayMode"/> fires while the worlds are still alive. By
    /// <c>EnteredEditMode</c> every world is disposed and the recordings are gone, so a saver hooked there writes
    /// nothing and reports success.
    ///
    /// WHY THE FOLDER IS GITIGNORED
    ///
    /// Every designer generates one of these on every play. Tracked, they would collide on every merge and carry no
    /// information anyone else wants. The folder is created on demand and listed in .gitignore.
    /// </remarks>
    [InitializeOnLoad]
    public static class InputAutoRecordSaver
    {
        private const string Prefix = "InputRecording_";

        static InputAutoRecordSaver()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.ExitingPlayMode || !MultiInputSettings.AutoRecordOrDefault)
            {
                return;
            }

            try
            {
                Save();
            }
            catch (Exception e)
            {
                // A failed save must never take play-mode exit down with it.
                Debug.LogError($"[PlayerInputs] auto-record save failed: {e.Message}");
            }
        }

        private static void Save()
        {
            var folder = MultiInputSettings.RecordFolderOrDefault;
            var saved = 0;

            foreach (var world in World.All)
            {
                if (!world.IsCreated)
                {
                    continue;
                }

                var em = world.EntityManager;
                using var query = em.CreateEntityQuery(
                    ComponentType.ReadOnly<InputRecording>(),
                    ComponentType.ReadOnly<AutoRecorded>());

                using var entities = query.ToEntityArray(Allocator.Temp);
                foreach (var entity in entities)
                {
                    var recording = em.GetComponentData<InputRecording>(entity);

                    // A session where nobody touched anything is not worth a file.
                    if (recording.FrameCount == 0 ||
                        (em.GetBuffer<RecordedEdge>(entity, true).Length == 0 &&
                         em.GetBuffer<RecordedAxisSample>(entity, true).Length == 0))
                    {
                        continue;
                    }

                    EnsureFolder(folder);

                    var asset = ScriptableObject.CreateInstance<InputRecordingAsset>();
                    InputRecordingTransfer.Save(em, entity, asset);

                    var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    var path = AssetDatabase.GenerateUniqueAssetPath(
                        $"{folder}/{Prefix}{stamp}_seat{recording.Seat}.asset");

                    AssetDatabase.CreateAsset(asset, path);
                    saved++;
                }
            }

            if (saved == 0)
            {
                return;
            }

            AssetDatabase.SaveAssets();
            Prune(folder);
            AssetDatabase.Refresh();

            Debug.Log($"[PlayerInputs] saved {saved} input recording(s) to {folder}. " +
                      "Replay one with InputReplay.OnSeat(seat) to drive a seat from it.");
        }

        /// <summary>Deletes the oldest recordings until at most <c>MaxStored</c> remain.</summary>
        private static void Prune(string folder)
        {
            var max = MultiInputSettings.MaxStoredOrDefault;

            var existing = AssetDatabase
                .FindAssets($"t:{nameof(InputRecordingAsset)}", new[] { folder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => Path.GetFileName(p).StartsWith(Prefix, StringComparison.Ordinal))
                // The timestamp is in the filename, so ordering by name orders by age without touching the disk.
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList();

            var excess = existing.Count - max;
            for (var i = 0; i < excess; i++)
            {
                AssetDatabase.DeleteAsset(existing[i]);
            }

            if (excess > 0)
            {
                Debug.Log($"[PlayerInputs] pruned {excess} old recording(s), keeping the newest {max}.");
            }
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            var parts = folder.Split('/');
            var path = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = $"{path}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(path, parts[i]);
                }

                path = next;
            }
        }

        /// <summary>Every stored recording, newest last. Used by the CLI and by anything offering a replay picker.</summary>
        public static IReadOnlyList<InputRecordingAsset> Stored()
        {
            var folder = MultiInputSettings.RecordFolderOrDefault;
            if (!AssetDatabase.IsValidFolder(folder))
            {
                return Array.Empty<InputRecordingAsset>();
            }

            return AssetDatabase
                .FindAssets($"t:{nameof(InputRecordingAsset)}", new[] { folder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(p => p, StringComparer.Ordinal)
                .Select(AssetDatabase.LoadAssetAtPath<InputRecordingAsset>)
                .Where(a => a != null)
                .ToList();
        }
    }
}
