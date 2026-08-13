using System.Collections.Generic;
using BovineLabs.Timeline.PlayerInputs.Data;
using Unity.Entities;
using UnityEngine;

namespace BovineLabs.Timeline.PlayerInputs
{
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [UpdateAfter(typeof(ProviderSyncSystem))]
    [UpdateBefore(typeof(ControlAuthoritySystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ClientSimulation)]
    public partial class InputReplaySystem : SystemBase
    {
        private readonly Dictionary<byte, PlayerInputBridge> disabledBridges = new();
        private readonly List<Entity> toProcess = new();

        protected override void OnUpdate()
        {
            var em = this.EntityManager;

            this.toProcess.Clear();
            foreach (var (replay, entity) in SystemAPI
                         .Query<RefRO<InputReplay>>()
                         .WithAll<InputRecording>()
                         .WithEntityAccess())
            {
                this.toProcess.Add(entity);
            }

            foreach (var entity in this.toProcess)
            {
                if (!em.Exists(entity) || !em.HasComponent<InputReplay>(entity))
                {
                    continue;
                }

                this.Process(em, entity);
            }
        }

        private void Process(EntityManager em, Entity entity)
        {
            var recording = em.GetComponentData<InputRecording>(entity);

            if (recording.FrameCount == 0)
            {
                Debug.LogWarning(
                    $"InputReplaySystem: recording for seat {recording.Seat} has no frames; nothing to replay.");
                em.RemoveComponent<InputReplay>(entity);
                return;
            }

            var replay = em.GetComponentData<InputReplay>(entity);

            // Where this recording actually plays. Retargeting is what turns a recording from a possession of its
            // original seat into an additional player on a free one.
            var seat = replay.RetargetSeat ? replay.Seat : recording.Seat;

            if (replay.Provider == Entity.Null)
            {
                // Only when we are taking a human's own seat. A replay provider registers as Human (it carries no
                // SyntheticProviderTag), so two of them on one seat would be a same-kind duplicate; on a free seat
                // there is nobody to displace and the human keeps playing.
                if (seat == recording.Seat)
                {
                    this.DisableHumanBridge(seat);
                }

                replay.Provider = CreateReplayProvider(em, seat);
                InputReplayMath.Reset(ref replay, recording.InitialHeld);
                em.SetComponentData(replay.Provider, new InputState { Held = replay.Held });
            }

            if (!em.Exists(replay.Provider))
            {
                this.RestoreHumanBridge(seat);
                em.RemoveComponent<InputReplay>(entity);
                return;
            }

            if (replay.Frame < recording.FrameCount)
            {
                var edges = em.GetBuffer<RecordedEdge>(entity);
                var samples = em.GetBuffer<RecordedAxisSample>(entity);

                var held = replay.Held;
                var edgeCursor = replay.EdgeCursor;
                InputReplayMath.StepFrame(edges, replay.Frame, ref edgeCursor, ref held, out var down, out var up);
                replay.EdgeCursor = edgeCursor;
                replay.Held = held;

                em.SetComponentData(replay.Provider, new InputState { Down = down, Held = held, Up = up });

                var outAxes = em.GetBuffer<InputAxis>(replay.Provider);
                var axisCursor = replay.AxisCursor;
                InputReplayMath.CollectAxes(samples, replay.Frame, ref axisCursor, outAxes);
                replay.AxisCursor = axisCursor;

                replay.Frame++;
                em.SetComponentData(entity, replay);
                return;
            }

            if (replay.Loop)
            {
                InputReplayMath.Reset(ref replay, recording.InitialHeld);
                em.SetComponentData(replay.Provider, new InputState { Held = replay.Held });
                em.GetBuffer<InputAxis>(replay.Provider).Clear();
                em.SetComponentData(entity, replay);
                return;
            }

            RetireReplayProvider(em, replay.Provider);
            this.RestoreHumanBridge(seat);
            em.RemoveComponent<InputReplay>(entity);
        }

        private static Entity CreateReplayProvider(EntityManager em, byte seat)
        {
            var provider = em.CreateEntity();
            em.AddComponentData(provider, new PlayerId { Value = seat });
            em.AddComponent<ProviderTag>(provider);
            em.AddComponent<InputState>(provider);
            em.AddBuffer<InputAxis>(provider);
            return provider;
        }

        private static void RetireReplayProvider(EntityManager em, Entity provider)
        {
            if (!em.Exists(provider))
            {
                return;
            }

            var held = em.GetComponentData<InputState>(provider).Held;
            em.SetComponentData(provider, new InputState { Up = held });
            em.GetBuffer<InputAxis>(provider).Clear();
            em.AddComponent<ProviderRetiring>(provider);
        }

        private void DisableHumanBridge(byte seat)
        {
            foreach (var (id, bridge) in SystemAPI
                         .Query<RefRO<PlayerId>, PlayerInputBridgeComponent>()
                         .WithAll<ProviderTag>()
                         .WithNone<ProviderRetiring>())
            {
                if (id.ValueRO.Value != seat || bridge.Value == null)
                {
                    continue;
                }

                bridge.Value.enabled = false;
                this.disabledBridges[seat] = bridge.Value;
                return;
            }
        }

        private void RestoreHumanBridge(byte seat)
        {
            if (!this.disabledBridges.TryGetValue(seat, out var bridge))
            {
                return;
            }

            if (bridge != null)
            {
                bridge.enabled = true;
            }

            this.disabledBridges.Remove(seat);
        }
    }
}
