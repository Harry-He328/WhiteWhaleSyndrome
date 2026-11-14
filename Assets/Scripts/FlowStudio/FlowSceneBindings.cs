using System;
using System.Collections.Generic;
using UnityEngine;
using WhiteWhale.Flow;

namespace WhiteWhale.FlowStudio
{
    [DisallowMultipleComponent, AddComponentMenu("Flow/Scene Binding")]
    public sealed class FlowSceneBindings : MonoBehaviour
    {
        [SerializeField, HideInInspector] private string id;
        [SerializeField] private string displayName;
        [SerializeField, HideInInspector] private FlowBinding[] objects;

        public IEnumerable<FlowBinding> Objects
        {
            get
            {
                if (!string.IsNullOrEmpty(id))
                    yield return new FlowBinding { key = "scene:" + id, target = gameObject, label = displayName };
                foreach (var binding in objects ?? Array.Empty<FlowBinding>()) yield return binding;
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (string.IsNullOrEmpty(id)) id = Guid.NewGuid().ToString("N");
        }
#endif
    }
}
