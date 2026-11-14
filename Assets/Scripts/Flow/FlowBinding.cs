using System;
using UnityEngine;

namespace WhiteWhale.Flow
{
    [Serializable]
    public sealed class FlowBinding
    {
        public string key;
        public string label;
        public GameObject target;
        public string DisplayName => string.IsNullOrWhiteSpace(label) ? target.name : label;
    }
}
