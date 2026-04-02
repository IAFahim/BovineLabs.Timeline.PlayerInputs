using System.Collections.Generic;
using PlayerInputs.PlayerInputs.Data;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PlayerInputs.PlayerInputs
{
    [RequireComponent(typeof(PlayerInput))]
    public class PlayerInputBridge : MonoBehaviour
    {
        public static readonly HashSet<PlayerInputBridge> Instances = new();

        [Header("Input Action References")] [SerializeField]
        private InputActionReference moveRef;

        [SerializeField] private InputActionReference lookRef;
        [SerializeField] private InputActionReference attackRef;
        [SerializeField] private InputActionReference interactRef;
        [SerializeField] private InputActionReference crouchRef;
        [SerializeField] private InputActionReference jumpRef;
        [SerializeField] private InputActionReference previousRef;
        [SerializeField] private InputActionReference nextRef;
        [SerializeField] private InputActionReference sprintRef;
        private InputAction _attackAction;

        private Entity _backingEntity;
        private InputAction _crouchAction;
        private EntityManager _entityManager;
        private byte _id;

        private InputAction _interactAction;
        private InputAction _jumpAction;
        private InputAction _lookAction;
        private InputAction _moveAction;
        private InputAction _nextAction;
        private InputAction _prevAction;
        private InputAction _sprintAction;

        private void Start()
        {
            var unityPlayerInput = GetComponent<PlayerInput>();
            _id = (byte)unityPlayerInput.playerIndex;

            _moveAction = ResolveAction(unityPlayerInput, moveRef);
            _lookAction = ResolveAction(unityPlayerInput, lookRef);
            _attackAction = ResolveAction(unityPlayerInput, attackRef);
            _interactAction = ResolveAction(unityPlayerInput, interactRef);
            _crouchAction = ResolveAction(unityPlayerInput, crouchRef);
            _jumpAction = ResolveAction(unityPlayerInput, jumpRef);
            _prevAction = ResolveAction(unityPlayerInput, previousRef);
            _nextAction = ResolveAction(unityPlayerInput, nextRef);
            _sprintAction = ResolveAction(unityPlayerInput, sprintRef);

            _entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;

            var archetype = _entityManager.CreateArchetype(
                typeof(BackingInputEntityTag),
                typeof(ECSPlayerInputID),
                typeof(InputSubscribedEntity), typeof(ECSPlayerInputActiveThisFrame),
                typeof(InputAttack), typeof(InputInteract), typeof(InputCrouch), typeof(InputJump),
                typeof(InputPrevious), typeof(InputNext), typeof(InputSprint),
                typeof(PlayerMoveInput), typeof(PlayerLookInput), typeof(ECSPlayerInputActivePreviousFrame),
                typeof(InputAttackPrevious), typeof(InputInteractPrevious), typeof(InputCrouchPrevious),
                typeof(InputJumpPrevious),
                typeof(InputPreviousPrevious), typeof(InputNextPrevious), typeof(InputSprintPrevious),
                typeof(PlayerMoveInputPrevious), typeof(PlayerLookPrevious),
                typeof(PlayerMoveInputActive), typeof(PlayerLookInputActive),
                typeof(PlayerMoveInputActivePrevious), typeof(PlayerLookInputActivePrevious)
            );

            _backingEntity = _entityManager.CreateEntity(archetype);
            _entityManager.SetComponentData(_backingEntity, new ECSPlayerInputID { ID = _id });

            DisableEnableables();
        }

        private void Update()
        {
            if (!_entityManager.Exists(_backingEntity)) return;

            float2 moveInput = _moveAction.ReadValue<Vector2>();
            float2 lookInput = _lookAction.ReadValue<Vector2>();

            var attackPressed = _attackAction.WasPressedThisFrame();
            var interactPressed = _interactAction.WasPressedThisFrame();
            var crouchHeld = _crouchAction.IsInProgress();
            var jumpPressed = _jumpAction.WasPressedThisFrame();
            var prevPressed = _prevAction.WasPressedThisFrame();
            var nextPressed = _nextAction.WasPressedThisFrame();
            var sprintHeld = _sprintAction.IsInProgress();

            var moveActive = math.lengthsq(moveInput) > 0;
            var lookActive = math.lengthsq(lookInput) > 0;

            var isActive = moveActive || lookActive ||
                           attackPressed || interactPressed || crouchHeld ||
                           jumpPressed || prevPressed || nextPressed || sprintHeld;

            _entityManager.SetComponentEnabled<ECSPlayerInputActiveThisFrame>(_backingEntity, isActive);

            _entityManager.SetComponentEnabled<InputAttack>(_backingEntity, attackPressed);
            _entityManager.SetComponentEnabled<InputInteract>(_backingEntity, interactPressed);
            _entityManager.SetComponentEnabled<InputCrouch>(_backingEntity, crouchHeld);
            _entityManager.SetComponentEnabled<InputJump>(_backingEntity, jumpPressed);
            _entityManager.SetComponentEnabled<InputPrevious>(_backingEntity, prevPressed);
            _entityManager.SetComponentEnabled<InputNext>(_backingEntity, nextPressed);
            _entityManager.SetComponentEnabled<InputSprint>(_backingEntity, sprintHeld);

            _entityManager.SetComponentEnabled<PlayerMoveInputActive>(_backingEntity, moveActive);
            _entityManager.SetComponentEnabled<PlayerLookInputActive>(_backingEntity, lookActive);

            _entityManager.SetComponentData(_backingEntity, new PlayerMoveInput { Value = moveInput });
            _entityManager.SetComponentData(_backingEntity, new PlayerLookInput { Value = lookInput });
        }

        private void OnEnable()
        {
            Instances.Add(this);
        }

        private void OnDisable()
        {
            Instances.Remove(this);

            if (World.DefaultGameObjectInjectionWorld != null &&
                World.DefaultGameObjectInjectionWorld.IsCreated &&
                _entityManager != default &&
                _entityManager.Exists(_backingEntity))
                _entityManager.DestroyEntity(_backingEntity);
        }

        private InputAction ResolveAction(PlayerInput playerInput, InputActionReference reference)
        {
            if (reference == null || reference.action == null) return null;
            return playerInput.actions.FindAction(reference.action.id);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instances.Clear();
        }

        private void DisableEnableables()
        {
            _entityManager.SetComponentEnabled<ECSPlayerInputActiveThisFrame>(_backingEntity, false);
            _entityManager.SetComponentEnabled<InputAttack>(_backingEntity, false);
            _entityManager.SetComponentEnabled<InputInteract>(_backingEntity, false);
            _entityManager.SetComponentEnabled<InputCrouch>(_backingEntity, false);
            _entityManager.SetComponentEnabled<InputJump>(_backingEntity, false);
            _entityManager.SetComponentEnabled<InputPrevious>(_backingEntity, false);
            _entityManager.SetComponentEnabled<InputNext>(_backingEntity, false);
            _entityManager.SetComponentEnabled<InputSprint>(_backingEntity, false);
            _entityManager.SetComponentEnabled<PlayerMoveInputActive>(_backingEntity, false);
            _entityManager.SetComponentEnabled<PlayerLookInputActive>(_backingEntity, false);

            _entityManager.SetComponentEnabled<ECSPlayerInputActivePreviousFrame>(_backingEntity, false);
            _entityManager.SetComponentEnabled<InputAttackPrevious>(_backingEntity, false);
            _entityManager.SetComponentEnabled<InputInteractPrevious>(_backingEntity, false);
            _entityManager.SetComponentEnabled<InputCrouchPrevious>(_backingEntity, false);
            _entityManager.SetComponentEnabled<InputJumpPrevious>(_backingEntity, false);
            _entityManager.SetComponentEnabled<InputPreviousPrevious>(_backingEntity, false);
            _entityManager.SetComponentEnabled<InputNextPrevious>(_backingEntity, false);
            _entityManager.SetComponentEnabled<InputSprintPrevious>(_backingEntity, false);
            _entityManager.SetComponentEnabled<PlayerMoveInputActivePrevious>(_backingEntity, false);
            _entityManager.SetComponentEnabled<PlayerLookInputActivePrevious>(_backingEntity, false);
        }
    }
}
