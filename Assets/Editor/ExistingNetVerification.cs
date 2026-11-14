using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using WhiteWhale.Combat;
using WhiteWhale.OceanItems;

public static class ExistingNetVerification
{
    public static string Status { get; private set; } = "Not started";
    private static readonly List<string> results = new List<string>();
    private static BattleController battle;
    private static PlayerPickup pickup;
    private static OceanItemTargetSource source;
    private static float next, deadline;
    private static int phase;
    private static void Check(bool ok, string message)
    { if (!ok) throw new Exception(message); results.Add("PASS " + message); }
    public static void Begin()
    {
        if (!EditorApplication.isPlaying) throw new Exception("Enter Play first.");
        results.Clear(); phase = 0; next = Time.time; deadline = Time.time + 20f;
        Status = "Running"; EditorApplication.update -= Tick; EditorApplication.update += Tick;
    }
    private static void Use(Item target)
    {
        var net = UnityEngine.Object.FindObjectsByType<OceanNetRule>(FindObjectsSortMode.None).First(n => !n.IsFlying);
        Check(pickup.TryPickUp(net.GetComponent<Rigidbody>()), "existing net remains pickable");
        Camera.main.transform.rotation = Quaternion.LookRotation(source.Project(target.GetComponent<Collider>().bounds.center) - Camera.main.transform.position);
        Physics.SyncTransforms();
        Check(pickup.TryGetUseTarget(out ItemUseTarget hit) && hit.Item == target, "shared ray selects " + target.itemName + " in original Ocean scene");
        pickup.PlaceHeldItem();
        Check(net.IsFlying && pickup.HeldItem == null, "existing net flies to selected ship");
    }
    private static void Tick()
    {
        try
        {
            if (!EditorApplication.isPlaying || Time.time > deadline) throw new Exception("Verification interrupted or timed out.");
            if (Time.time < next) return;
            if (phase == 0)
            {
                battle = UnityEngine.Object.FindFirstObjectByType<BattleController>();
                if (battle == null || battle.State != BattleStatus.Running) return;
                battle.EnemyAutoFire = false;
                pickup = UnityEngine.Object.FindFirstObjectByType<PlayerPickup>(); source = pickup.GetComponent<OceanItemTargetSource>();
                var nets = UnityEngine.Object.FindObjectsByType<OceanNetRule>(FindObjectsSortMode.None);
                Check(nets.Length == 4 && nets.All(n => n.name.StartsWith("Pickup Test Item")), "only the four original scene nets exist");
                Check(nets.All(n => n.GetComponent<Rigidbody>().mass == 1f && n.transform.localScale == Vector3.one * 0.4f && n.GetComponent<BoxCollider>() != null), "original net scale, rigidbody mass and collider retained");
                Check(nets.All(n => n.GetComponentInChildren<SpriteRenderer>().sprite != null && !n.GetComponent<MeshRenderer>().enabled), "sprite replaces original mesh appearance on all existing nets");
                Check(nets.All(n => LabelSystem.HasLabel(n.GetComponent<Item>(), ItemLabel.CaptureAnimal)), "original capture labels retained");
                var animal = UnityEngine.Object.FindObjectsByType<Item>(FindObjectsSortMode.None).First(i => LabelSystem.HasLabel(i, ItemLabel.Animal));
                Check(pickup.TryPickUp(nets[0].GetComponent<Rigidbody>()) && ItemInteraction.TryInteract(pickup, nets[0].GetComponent<Item>(), animal) && pickup.HeldItem == animal, "existing net still captures existing monkey"); pickup.Drop();
                pickup.GetComponent<FirstPersonController>().enabled = false;
                var controller = pickup.GetComponent<CharacterController>(); controller.enabled = false;
                pickup.transform.position = new Vector3(0.5f, 0.03f, 4.1f); controller.enabled = true;
                Use(battle.Enemy.GetComponent<Item>()); next = Time.time + 0.8f; phase = 1; return;
            }
            if (phase == 1)
            {
                Check(battle.Enemy.GetComponent<MovementSpeedModifier>().Multiplier == 0.5f, "original net slows enemy on arrival");
                Use(battle.Player.GetComponent<Item>()); next = Time.time + 0.8f; phase = 2; return;
            }
            if (phase == 2)
            {
                Check(battle.Player.GetComponent<MovementSpeedModifier>().Multiplier == 0.5f, "original net slows player on arrival");
                battle.Player.SetSailAmount(1f); next = Time.time + 1.6f; phase = 3; return;
            }
            if (phase == 3)
            {
                float expected = new SerializedObject(battle.Player).FindProperty("maxSpeed").floatValue * 0.5f;
                Check(Mathf.Abs(battle.Player.CurrentSpeed - expected) < 0.15f, "player full-sail speed is halved relative to current sailing configuration");
                Finish(null);
            }
        }
        catch (Exception exception) { Finish(exception.Message); }
    }
    private static void Finish(string error)
    {
        EditorApplication.update -= Tick;
        Status = error == null ? "PASS" : "FAIL " + error; results.Add(Status);
        File.WriteAllLines("Captures/ExistingNetVerification.txt", results);
        Debug.Log("Existing net verification: " + Status);
    }
}
