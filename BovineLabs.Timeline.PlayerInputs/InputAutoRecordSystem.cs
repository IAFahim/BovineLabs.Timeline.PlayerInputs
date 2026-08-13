using System.Collections.Generic;
using BovineLabs.Timeline.PlayerInputs.Data;
using Unity.Entities;
using UnityEngine;

namespace BovineLabs.Timeline.PlayerInputs
{
    /// <summary>
    /// Arms a recording for every seat that joins, so a play session is captured without anyone remembering to press
    /// record.
    /// </summary>
    /// <remarks>
    /// WHY IT ARMS PER SEAT RATHER THAN ONCE
    ///
    /// A recording carries one seat's stream. Local co-op, a phone joining, a second pad — each is its own seat and
    /// its own recording, and seats appear at different moments rather than all at startup. So this watches for
    /// providers and arms the ones it has not seen, instead of arming a fixed set on the first frame.
    ///
    /// WHY IT COUNTS FRAMES
    ///
    /// <c>RecordStartFrame</c> exists because the frames between entering play and being in-game are menu clicks and
    /// loading, and replaying them replays the menu. Frames rather than seconds because the server tick and the
    /// physics step are frame-based: a delay in seconds would land on a different tick at a different framerate, and
    /// a recording that starts at a different tick is a different recording.
    ///
    /// WHY IT DOES NOT SAVE
    ///
    /// Writing a ScriptableObject needs the AssetDatabase, which does not exist in a player and must not be called
    /// from a system. Capture is runtime; persisting it is an editor concern and lives in InputAutoRecordSaver.
    /// </remarks>
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [UpdateAfter(typeof(InputRecordSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ClientSimulation)]
    public partial class InputAutoRecordSystem : SystemBase
    {
        private readonly HashSet<byte> armed = new();
        private readonly List<byte> pending = new();
        private uint frame;

        /// <summary>Seats this system has armed a recording for, so a saver can find them without guessing.</summary>
        public IReadOnlyCollection<byte> ArmedSeats => this.armed;

        /// <inheritdoc/>
        protected override void OnCreate()
        {
            // Capture is a play-mode idea. In edit mode the providers that exist are authoring artefacts, and
            // recording them produces a file that replays nothing.
            if (!Application.isPlaying)
            {
                this.Enabled = false;
            }
        }

        /// <inheritdoc/>
        protected override void OnUpdate()
        {
            if (!MultiInputSettings.AutoRecordOrDefault)
            {
                this.Enabled = false;
                return;
            }

            if (this.frame++ < MultiInputSettings.RecordStartFrameOrDefault)
            {
                return;
            }

            // Collect first, create after. CreateEntity is a structural change, and making one while iterating a
            // query invalidates that iteration — the seat ends up marked as armed with no recording behind it, which
            // is silent and permanent because the seat is never revisited.
            this.pending.Clear();

            foreach (var id in SystemAPI
                         .Query<RefRO<PlayerId>>()
                         .WithAll<ProviderTag, InputState>()
                         .WithNone<ProviderRetiring>())
            {
                var seat = id.ValueRO.Value;
                if (!this.armed.Contains(seat))
                {
                    this.pending.Add(seat);
                }
            }

            foreach (var seat in this.pending)
            {
                if (!this.armed.Add(seat))
                {
                    continue;
                }

                var recording = this.EntityManager.CreateEntity();
                this.EntityManager.SetName(recording, $"auto recording seat {seat}");
                this.EntityManager.AddComponentData(recording, new InputRecording { Seat = seat });
                this.EntityManager.AddBuffer<RecordedEdge>(recording);
                this.EntityManager.AddBuffer<RecordedAxisSample>(recording);
                this.EntityManager.AddComponent<InputRecordingActive>(recording);
                this.EntityManager.AddComponent<AutoRecorded>(recording);
            }
        }
    }

    /// <summary>Marks a recording this system created, so the saver persists those and leaves hand-made ones alone.</summary>
    public struct AutoRecorded : IComponentData
    {
    }
}
