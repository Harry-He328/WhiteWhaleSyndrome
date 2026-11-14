using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WhiteWhale.Flow
{
    /// <summary>Changes an item's presentation and physics while preserving its identity.</summary>
    [RequireComponent(typeof(Item), typeof(Rigidbody))]
    public sealed class FlowTransferable : MonoBehaviour
    {
        public GameObject oceanAppearance;
        public GameObject roomAppearance;
        public Vector3 roomScale = Vector3.one;
        public bool IsInRoom { get; private set; }

        public void MoveToRoom(Transform destination)
        {
            Scene previous = gameObject.scene;
            transform.SetParent(null, true);
            if (previous != destination.gameObject.scene) SceneManager.MoveGameObjectToScene(gameObject, destination.gameObject.scene);
            transform.SetPositionAndRotation(destination.position, destination.rotation);
            transform.localScale = roomScale;
            if (oceanAppearance != null) oceanAppearance.SetActive(false);
            if (roomAppearance != null) roomAppearance.SetActive(true);
            Rigidbody body = GetComponent<Rigidbody>();
            body.isKinematic = false;
            body.useGravity = true;
            body.position = destination.position;
            body.rotation = destination.rotation;
            body.linearVelocity = body.angularVelocity = Vector3.zero;
            foreach (Collider collider in GetComponents<Collider>()) collider.enabled = true;
            AgileMovement agile = GetComponent<AgileMovement>();
            if (agile != null) agile.enabled = true;
            IsInRoom = true;
            OceanVisualEvents.Notify(previous);
        }

        public Action CaptureRestore()
        {
            Transform parent = transform.parent;
            Scene scene = gameObject.scene;
            Vector3 position = transform.localPosition, scale = transform.localScale;
            Quaternion rotation = transform.localRotation;
            Rigidbody body = GetComponent<Rigidbody>();
            bool kinematic = body.isKinematic, gravity = body.useGravity, inRoom = IsInRoom;
            bool oceanActive = oceanAppearance != null && oceanAppearance.activeSelf;
            bool roomActive = roomAppearance != null && roomAppearance.activeSelf;
            AgileMovement agile = GetComponent<AgileMovement>();
            bool agileEnabled = agile != null && agile.enabled;
            Collider[] colliders = GetComponents<Collider>();
            bool[] enabled = Array.ConvertAll(colliders, c => c.enabled);
            return () =>
            {
                if (this == null) return;
                foreach (PlayerPickup pickup in FindObjectsByType<PlayerPickup>(FindObjectsSortMode.None))
                    if (pickup.HeldBody == body) pickup.Drop();
                Scene previous = gameObject.scene;
                transform.SetParent(null, true);
                if (scene.isLoaded && previous != scene) SceneManager.MoveGameObjectToScene(gameObject, scene);
                transform.SetParent(parent, false);
                transform.localPosition = position; transform.localRotation = rotation; transform.localScale = scale;
                if (!body.isKinematic) body.linearVelocity = body.angularVelocity = Vector3.zero;
                body.isKinematic = kinematic; body.useGravity = gravity;
                body.position = transform.position; body.rotation = transform.rotation;
                for (int i = 0; i < colliders.Length; i++) if (colliders[i] != null) colliders[i].enabled = enabled[i];
                if (agile != null) agile.enabled = agileEnabled;
                if (oceanAppearance != null) oceanAppearance.SetActive(oceanActive);
                if (roomAppearance != null) roomAppearance.SetActive(roomActive);
                IsInRoom = inRoom;
                OceanVisualEvents.Notify(previous); OceanVisualEvents.Notify(gameObject.scene);
            };
        }
    }
}
