using System;
using BovineLabs.Core.Authoring.EntityCommands;
using BovineLabs.Reaction.Authoring.Conditions;
using BovineLabs.Reaction.Data.Conditions;
using BovineLabs.Reaction.Data.Core;
using BovineLabs.Timeline.Authoring;
using BovineLabs.Timeline.EntityLinks.Authoring;
using BovineLabs.Timeline.PlayerInputs.Data;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Timeline;

namespace BovineLabs.Timeline.PlayerInputs.Authoring
{
    [Serializable]
    public struct CommandStepData
    {
        [Tooltip("Input action this step must match.")]
        public InputActionReference Action;

        [Tooltip("How this step reads history: None probes live state, Contains/Consume families match buffered transitions.")]
        public CommandMode Mode;

        [Tooltip("Which transition to match: Down on press, Up on release, Held while sustained.")]
        public InputPhase Phase;

        [Tooltip("Max simulation ticks allowed between this step and the previous matched step. " +
                 "0 = unbounded. Use this to author motion inputs and frame links (e.g. a 236P fireball " +
                 "where each direction must follow within a few ticks).")]
        public ushort MaxGapTicks;
    }

    [Serializable]
    public class CommandSequenceData
    {
        [Tooltip("Ordered steps that must all match for this sequence to fire.")]
        public CommandStepData[] Steps = Array.Empty<CommandStepData>();

        [Tooltip("Condition event fired at the routed entity when the sequence matches.")]
        public ConditionEventObject Condition;

        [Tooltip("Value carried by the fired condition event.")]
        public int Value = 1;

        [Tooltip("On: the sequence re-arms after firing and can trigger again while the clip stays active " +
                 "(a dash you can do repeatedly). Off: fires once per clip activation.")]
        public bool Repeatable = true;
    }

    [Tooltip(
        "Sequences are evaluated top-to-bottom. High priority should be first. First successful match triggers event and completes sequence clip.")]
    public sealed class CommandSequenceClip : DOTSClip, ITimelineClipAsset
    {
        [Tooltip("Sequences evaluated top-to-bottom; the first that matches fires and completes the clip.")]
        public CommandSequenceData[] Sequences = Array.Empty<CommandSequenceData>();

        [Header("Consumer")]
        [Tooltip("Where to resolve the entity that owns the ConsumerLink from.")]
        public Target ReadRootFrom = Target.Owner;

        [Tooltip("Link to the input consumer whose history/state these sequences read.")]
        public EntityLinkSchema ConsumerLink;

        [Header("Events")]
        [Tooltip("Where to resolve the entity that receives the fired events from.")]
        public Target EventRouteTo = Target.Self;

        [Tooltip("Link used to resolve the event target when EventRouteTo needs one.")]
        public EntityLinkSchema EventRouteLink;

        public override double duration => .5f;
        public ClipCaps clipCaps => ClipCaps.None;

        public override void Bake(Entity entity, BakingContext context)
        {
            MultiInputSettingsAuthoringUtility.DependsOnSettings(context.Baker);

            if (!EntityLinkAuthoringUtility.TryGetKey(ConsumerLink, out var consumerLinkKey))
            {
                Debug.LogError($"CommandSequenceClip '{name}' missing ConsumerLink schema.", this);
                return;
            }

            EntityLinkAuthoringUtility.TryGetKey(EventRouteLink, out var eventRouteLinkKey);

            var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<CommandBlob>();
            var seqArray = builder.Allocate(ref root.Sequences, Sequences.Length);

            for (var s = 0; s < Sequences.Length; s++)
            {
                var seqData = Sequences[s];
                seqArray[s].Condition = seqData.Condition ? seqData.Condition.Key : ConditionKey.Null;
                seqArray[s].Value = seqData.Value;
                seqArray[s].Repeat = seqData.Repeatable ? (byte)1 : (byte)0;

                if (seqData.Repeatable && HasHistoryStepWithoutConsume(seqData.Steps))
                    Debug.LogWarning(
                        $"CommandSequenceClip '{name}' sequence {s} is Repeatable but no step consumes history. " +
                        "It will retrigger every frame while the history still matches. Use a Consume-family " +
                        "mode on at least one step, or disable Repeatable.", this);

                var stepArray = builder.Allocate(ref seqArray[s].Steps, seqData.Steps.Length);
                for (var i = 0; i < seqData.Steps.Length; i++)
                {
                    var actionRef = seqData.Steps[i].Action;

                    // A null or unresolved action must FAIL CLOSED, not silently bake to ActionId 0
                    // (which would gate the step on whatever action is index 0). byte.MaxValue can never
                    // match a real action in any mode (live-state bitsets are 256-wide; buffered history
                    // only ever holds real ids), so the misconfigured sequence becomes inert.
                    byte id = byte.MaxValue;
                    if (actionRef == null)
                    {
                        Debug.LogError(
                            $"CommandSequenceClip '{name}' sequence {s} step {i} has no Action assigned; " +
                            "the step can never match and the sequence will not fire.", this);
                    }
                    else if (!MultiInputSettingsAuthoringUtility.TryGetIndex(actionRef, out id))
                    {
                        id = byte.MaxValue;
                        Debug.LogError(
                            $"CommandSequenceClip '{name}' action '{actionRef.name}' not found in MultiInputSettings; " +
                            "the step can never match.", this);
                    }

                    var stepMode = seqData.Steps[i].Mode;
                    var stepPhase = seqData.Steps[i].Phase;
                    if (id == byte.MaxValue)
                    {
                        // FAIL CLOSED for EVERY mode: force a live-state probe (Mode.None) of the reserved
                        // id 255. Buffered positive modes are inert on a missing id, but the Not* family
                        // PASSES on absence (would fail OPEN and fire every frame) — a live probe of the
                        // never-set bit 255 is inert in all modes. (Reserves action index 255 as the sentinel.)
                        stepMode = CommandMode.None;
                    }
                    else if (stepMode != CommandMode.None && stepPhase == InputPhase.Held)
                        Debug.LogError(
                            $"CommandSequenceClip '{name}' step {i} uses a buffered mode with the Held " +
                            "phase, which can never match: input history records Down/Up transitions only. " +
                            "Use CommandMode.None for a sustained-hold probe.", this);

                    // No-surprise guardrail: MaxGapTicks only gates buffered match modes against the PREVIOUS
                    // matched step. It is inert on the first step (no previous match), on None (live probe), and
                    // on the Not* family (absence checks) - warn so a designer never assumes a window is enforced.
                    var maxGap = seqData.Steps[i].MaxGapTicks;
                    if (maxGap != 0 && id != byte.MaxValue &&
                        (i == 0 || stepMode is CommandMode.None or CommandMode.NotContains
                            or CommandMode.NotFirst or CommandMode.NotLast))
                    {
                        Debug.LogWarning(
                            $"CommandSequenceClip '{name}' sequence {s} step {i}: MaxGapTicks={maxGap} has no effect " +
                            "here. The timing window only applies to buffered match modes on a step after the first; " +
                            "first steps, None probes and Not* modes ignore it.", this);
                    }

                    stepArray[i] = new CommandStep
                    {
                        ActionId = id,
                        Mode = stepMode,
                        Phase = stepPhase,
                        MaxGapTicks = seqData.Steps[i].MaxGapTicks
                    };
                }
            }

            var blobRef = builder.CreateBlobAssetReference<CommandBlob>(Allocator.Persistent);
            builder.Dispose();

            var commands = new BakerCommands(context.Baker, entity);
            commands.AddBlobAsset(ref blobRef, out _);
            commands.AddComponent(new CommandSequenceConfig
            {
                Blob = blobRef,
                ReadRootFrom = ReadRootFrom,
                ConsumerLinkKey = consumerLinkKey,
                EventRouteTo = EventRouteTo,
                EventRouteLinkKey = eventRouteLinkKey
            });
            commands.AddComponent<CommandSequenceState>();

            base.Bake(entity, context);
        }

        private static bool HasHistoryStepWithoutConsume(CommandStepData[] steps)
        {
            var hasHistoryStep = false;
            foreach (var step in steps)
            {
                switch (step.Mode)
                {
                    case CommandMode.Consume:
                    case CommandMode.FirstConsume:
                    case CommandMode.LastConsume:
                    case CommandMode.OrderedConsume:
                    case CommandMode.OrderedFirstConsume:
                    case CommandMode.OrderedLastConsume:
                        return false;
                    case CommandMode.Contains:
                    case CommandMode.OrderedContains:
                        hasHistoryStep = true;
                        break;
                }
            }

            return hasHistoryStep;
        }
    }
}