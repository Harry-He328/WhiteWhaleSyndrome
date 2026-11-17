using UnityEngine;
using UnityEngine.InputSystem;
using WhiteWhale.FlowStudio;

namespace WhiteWhale.Combat
{
    /// <summary>Connects the existing room to its additively loaded ocean.</summary>
    public sealed class RoomBattleConnection : MonoBehaviour
    {
        public OceanRoomBridge bridge;
        public BattleDefinition definition;
        [SerializeField] private bool runMainFlow;
        private FlowEventRunner eventRunner;
        public BattleController Controller { get; private set; }

        private void Update()
        {
            if (Controller != null)
            {
                HandleFlowInput();
                return;
            }
            if (bridge == null || !bridge.IsReady) return;
            var ship = bridge.Ship;
            var surface = ship.transform.root.GetComponentInChildren<OceanSurface>();
            Controller = gameObject.AddComponent<BattleController>();
            Controller.Initialize(definition, ship, surface, !runMainFlow);
            if (runMainFlow)
            {
                FlowSceneChanges.Changed += OceanVisualEvents.Notify;
                FlowSceneChanges.ObjectRemoving += DropRemovedItem;
                eventRunner = gameObject.AddComponent<FlowEventRunner>();
                eventRunner.StartFlow(gameObject.scene, ship.gameObject.scene);
            }
        }

        private void HandleFlowInput()
        {
            var keys = Keyboard.current;
            if (!Application.isFocused || keys == null || Controller.Player == null) return;
            if (keys.f5Key.wasPressedThisFrame)
            {
                if (eventRunner != null) eventRunner.RestartEvent();
                else Controller.RestartBattle();
            }
            if (keys.enterKey.wasPressedThisFrame) eventRunner?.Confirm();
            if (keys.digit1Key.wasPressedThisFrame || keys.numpad1Key.wasPressedThisFrame) eventRunner?.Choose(true);
            if (keys.digit2Key.wasPressedThisFrame || keys.numpad2Key.wasPressedThisFrame) eventRunner?.Choose(false);
        }

        private static void DropRemovedItem(GameObject item)
        {
            if (!item.TryGetComponent(out Rigidbody body)) return;
            foreach (var pickup in FindObjectsByType<PlayerPickup>(FindObjectsSortMode.None))
                if (pickup.HeldBody == body) pickup.Drop();
        }

        private void OnDisable()
        {
            if (eventRunner == null) return;
            eventRunner.enabled = false;
            FlowSceneChanges.Changed -= OceanVisualEvents.Notify;
            FlowSceneChanges.ObjectRemoving -= DropRemovedItem;
        }

        private void OnEnable()
        {
            if (eventRunner == null) return;
            FlowSceneChanges.Changed += OceanVisualEvents.Notify;
            FlowSceneChanges.ObjectRemoving += DropRemovedItem;
            eventRunner.enabled = true;
        }
    }
}
