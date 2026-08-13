using UnityEngine;

namespace BovineLabs.Timeline.PlayerInputs.Data
{
    /// <summary>
    /// Auto-recording half of the input settings: every play session is captured so it can be replayed later.
    /// </summary>
    /// <remarks>
    /// Deliberately a separate partial file rather than fields added to MultiInputSettings.cs — that file belongs to
    /// the package's input authoring, this is a capture concern, and keeping them apart means neither has to be
    /// merged against the other.
    /// <para>
    /// Everything here counts in FRAMES, never seconds. The server ticks and the physics step are frame-based, so a
    /// recording measured in seconds would replay differently at a different framerate and stop reproducing what it
    /// captured — which is the entire value of having it.
    /// </para>
    /// </remarks>
    public sealed partial class MultiInputSettings
    {
        [Header("Auto Recording")]
        [SerializeField]
        [Tooltip("Capture every play session automatically, so the last few runs can always be replayed. On by " +
                 "default: a session you did not think to record is the one you end up wanting.")]
        private bool autoRecord = true;

        [SerializeField]
        [Tooltip("How many recordings to keep. The oldest is deleted once this many exist, so the folder cannot grow " +
                 "without bound across a long day of play testing.")]
        [Min(1)]
        private int maxStoredRecordings = 10;

        [SerializeField]
        [Tooltip("Frames to wait after going in-game before capture starts. Menu clicks and the frames spent loading " +
                 "are not gameplay, and replaying them just replays the menu. Counted in frames because everything " +
                 "downstream is.")]
        [Min(0)]
        private int recordStartFrame;

        [SerializeField]
        [Tooltip("Where recordings are written. This folder should be gitignored — every designer generates their " +
                 "own on every play, and they would collide constantly in version control.")]
        private string recordFolder = "Assets/Recordings/Input";

        /// <summary>Whether every play session is captured automatically.</summary>
        public bool AutoRecord => this.autoRecord;

        /// <summary>How many recordings to keep before the oldest is pruned.</summary>
        public int MaxStoredRecordings => Mathf.Max(1, this.maxStoredRecordings);

        /// <summary>Frames to wait after going in-game before capture starts.</summary>
        public uint RecordStartFrame => (uint)Mathf.Max(0, this.recordStartFrame);

        /// <summary>Folder recordings are written to. Gitignored.</summary>
        public string RecordFolder =>
            string.IsNullOrWhiteSpace(this.recordFolder) ? "Assets/Recordings/Input" : this.recordFolder;

        /// <summary>Settings if an asset exists, otherwise the defaults — capture must not require configuring it.</summary>
        public static bool AutoRecordOrDefault => I == null || I.autoRecord;

        /// <summary>Max stored recordings if an asset exists, otherwise the default.</summary>
        public static int MaxStoredOrDefault => I == null ? 10 : I.MaxStoredRecordings;

        /// <summary>Start frame if an asset exists, otherwise 0.</summary>
        public static uint RecordStartFrameOrDefault => I == null ? 0u : I.RecordStartFrame;

        /// <summary>Record folder if an asset exists, otherwise the default.</summary>
        public static string RecordFolderOrDefault => I == null ? "Assets/Recordings/Input" : I.RecordFolder;
    }
}
