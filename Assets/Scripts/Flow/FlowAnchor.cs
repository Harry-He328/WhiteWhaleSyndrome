using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace WhiteWhale.Flow
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Flow/Anchor")]
    public sealed class FlowAnchor : MonoBehaviour
    {
        [SerializeField, HideInInspector] private string id;
        public string Id => id;

        public static IEnumerable<FlowAnchor> On(Transform owner) => owner != null
            ? owner.GetComponentsInChildren<FlowAnchor>(true).Where(anchor => !string.IsNullOrEmpty(anchor.Id))
            : Enumerable.Empty<FlowAnchor>();

        public static Transform Find(Transform owner, string id)
        {
            if (string.IsNullOrEmpty(id)) return owner;
            var matches = On(owner).Where(anchor => anchor.Id == id).Take(2).ToArray();
            return matches.Length == 1 ? matches[0].transform : null;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (string.IsNullOrEmpty(id)) id = Guid.NewGuid().ToString("N");
        }
#endif
    }
}
