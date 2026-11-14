using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WhiteWhale.FlowStudio
{
    public static class FlowSceneChanges
    {
        public static event Action<Scene> Changed;
        public static event Action<GameObject> ObjectRemoving;

        internal static void Notify(Scene scene) => Changed?.Invoke(scene);
        internal static void Removing(GameObject instance) => ObjectRemoving?.Invoke(instance);
    }
}
