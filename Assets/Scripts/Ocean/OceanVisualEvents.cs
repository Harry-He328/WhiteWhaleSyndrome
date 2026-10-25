using System;
using UnityEngine.SceneManagement;

/// <summary>Spawn/despawn notification, independent of any tabletop display.</summary>
public static class OceanVisualEvents
{
    public static event Action<Scene> Changed;
    public static void Notify(Scene scene) => Changed?.Invoke(scene);
}
